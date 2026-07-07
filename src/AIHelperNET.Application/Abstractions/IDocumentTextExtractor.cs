using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Port for extracting plain text from a document file.
/// Supported formats: .pdf, .docx, .txt, .md.
/// </summary>
public interface IDocumentTextExtractor
{
    /// <summary>
    /// Extracts the plain text content from <paramref name="filePath"/>.
    /// Returns a failed <see cref="Result{T}"/> for unsupported extensions,
    /// missing files, empty results, or corrupt documents — never throws for
    /// expected failures.
    /// </summary>
    /// <param name="filePath">Absolute path to the document file.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<string>> ExtractAsync(string filePath, CancellationToken ct);
}
