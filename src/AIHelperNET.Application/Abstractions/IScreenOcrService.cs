using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for capturing the screen and extracting text via OCR.</summary>
public interface IScreenOcrService
{
    /// <summary>Captures the current screen and returns the extracted text.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<string>> CaptureAndReadAsync(CancellationToken ct);
}
