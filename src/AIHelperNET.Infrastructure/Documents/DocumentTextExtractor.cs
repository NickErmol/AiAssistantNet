using System.IO;
using AIHelperNET.Application.Abstractions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentResults;
using UglyToad.PdfPig;

namespace AIHelperNET.Infrastructure.Documents;

/// <summary>
/// Extracts plain text from PDF, DOCX, TXT and MD files.
/// All expected failures are returned as failed Results — never thrown.
/// </summary>
public sealed class DocumentTextExtractor : IDocumentTextExtractor
{
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".docx", ".txt", ".md" };

    public async Task<Result<string>> ExtractAsync(string filePath, CancellationToken ct)
    {
        var ext = Path.GetExtension(filePath);

        if (!SupportedExtensions.Contains(ext))
            return Result.Fail($"Unsupported file type: {ext}. Use PDF, DOCX, TXT or MD.");

        if (!File.Exists(filePath))
            return Result.Fail($"File not found: {filePath}");

        if (new FileInfo(filePath).Length > 10 * 1024 * 1024)
            return Result.Fail("File is too large (max 10 MB).");

        try
        {
            string text;
            var extLower = ext.ToLowerInvariant();
            if (extLower is ".txt" or ".md")
            {
                text = await File.ReadAllTextAsync(filePath, ct);
            }
            else if (extLower == ".pdf")
            {
                text = await Task.Run(() => ExtractPdf(filePath, ct), ct);
            }
            else
            {
                text = await Task.Run(() => ExtractDocx(filePath), ct);
            }

            if (string.IsNullOrWhiteSpace(text))
                return Result.Fail("No text could be extracted from the file.");

            return Result.Ok(text);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Result.Fail($"Failed to read file: {ex.Message}");
        }
    }

    private static string ExtractPdf(string filePath, CancellationToken ct)
    {
        using var document = PdfDocument.Open(filePath);
        var sb = new System.Text.StringBuilder();
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            if (!string.IsNullOrEmpty(page.Text))
                sb.AppendLine(page.Text);
        }
        return sb.ToString().TrimEnd();
    }

    private static string ExtractDocx(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return string.Empty;

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrEmpty(t));
        return string.Join("\n", paragraphs);
    }
}
