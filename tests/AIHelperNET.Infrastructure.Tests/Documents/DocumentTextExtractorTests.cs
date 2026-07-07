using System.IO;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Documents;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Documents;

public sealed class DocumentTextExtractorTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly DocumentTextExtractor _sut = new();

    public DocumentTextExtractorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best effort */ }
    }

    // ─── .txt round-trip ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_TxtFile_RoundTripsContent()
    {
        const string expected = "Hello from a plain text file.";
        var path = Path.Combine(_tempDir, "resume.txt");
        await File.WriteAllTextAsync(path, expected);

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(expected);
    }

    // ─── .md round-trip ───────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_MdFile_RoundTripsContent()
    {
        const string expected = "# Senior Engineer\n\nSpecialises in distributed systems.";
        var path = Path.Combine(_tempDir, "profile.md");
        await File.WriteAllTextAsync(path, expected);

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("Senior Engineer");
        result.Value.Should().Contain("distributed systems");
    }

    // ─── .docx extraction ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_DocxFile_ExtractsParagraphText()
    {
        const string paragraphText = "Senior C# developer at Contoso";
        var path = Path.Combine(_tempDir, "resume.docx");
        CreateDocx(path, paragraphText);

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(paragraphText);
    }

    // ─── .pdf extraction ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_PdfFile_ExtractsPageText()
    {
        const string pdfText = "Azure API Management experience";
        var path = Path.Combine(_tempDir, "resume.pdf");
        CreatePdf(path, pdfText);

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(pdfText);
    }

    // ─── unsupported extension → fail ─────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_UnsupportedExtension_ReturnsFail()
    {
        var path = Path.Combine(_tempDir, "data.xlsx");
        await File.WriteAllBytesAsync(path, [0x50, 0x4B]); // fake xlsx bytes

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should()
            .Contain("Unsupported");
    }

    // ─── missing file → fail ─────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_MissingFile_ReturnsFail()
    {
        var path = Path.Combine(_tempDir, "does_not_exist.txt");

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // ─── empty file → fail ────────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_EmptyTxtFile_ReturnsFail()
    {
        var path = Path.Combine(_tempDir, "empty.txt");
        await File.WriteAllTextAsync(path, string.Empty);

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should()
            .Contain("No text could be extracted");
    }

    // ─── oversize file → fail ─────────────────────────────────────────────────

    [Fact]
    public async Task ExtractAsync_OversizeFile_ReturnsFail()
    {
        var path = Path.Combine(_tempDir, "huge.txt");
        // Write an 11 MB file (beyond 10 MB guard)
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.SetLength(11L * 1024 * 1024);
        // Write some bytes so it's a real file, not sparse (sparse may not work on all FS)
        fs.Seek(0, SeekOrigin.Begin);
        await fs.WriteAsync(new byte[1024]);
        await fs.FlushAsync();

        var result = await _sut.ExtractAsync(path, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should().Contain("too large");
    }

    // ─── pre-cancelled token → OperationCanceledException ────────────────────

    [Fact]
    public async Task ExtractAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        const string pdfText = "cancellation test content";
        var path = Path.Combine(_tempDir, "cancel_test.pdf");
        CreatePdf(path, pdfText);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _sut.ExtractAsync(path, cts.Token));
    }

    // ─── Fixture helpers ──────────────────────────────────────────────────────

    private static void CreateDocx(string path, string text)
    {
        using var doc = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var mainPart = doc.AddMainDocumentPart();
        mainPart.Document = new Document(
            new Body(
                new Paragraph(
                    new Run(
                        new Text(text)))));
        mainPart.Document.Save();
    }

    private static void CreatePdf(string path, string text)
    {
        var builder = new PdfDocumentBuilder();
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        page.AddText(text, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 700), font);
        var bytes = builder.Build();
        File.WriteAllBytes(path, bytes);
    }
}
