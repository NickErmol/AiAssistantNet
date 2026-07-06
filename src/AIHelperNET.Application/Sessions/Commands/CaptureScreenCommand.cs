using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to capture the current screen and extract text via OCR.</summary>
public sealed record CaptureScreenCommand : IRequest<Result<string>>;

/// <summary>Handles <see cref="CaptureScreenCommand"/>.</summary>
public sealed class CaptureScreenHandler(IScreenOcrService ocrService)
    : IRequestHandler<CaptureScreenCommand, Result<string>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<string>> Handle(CaptureScreenCommand command, CancellationToken cancellationToken)
    {
        var result = await ocrService.CaptureAndReadAsync(cancellationToken);
        if (result.IsFailed) return result;

        // Distinct diagnoses: an empty OCR is a blank/unreadable screen, not chrome.
        if (string.IsNullOrWhiteSpace(result.Value))
            return Result.Fail<string>("Capture rejected: OCR returned no text (blank or unreadable screen).");

        // A capture of meeting/window UI chrome (title bar, participant list) carries no answerable
        // task; creating a screen turn from it would also make that garbage the "task in focus" and
        // misroute subsequent interviewer speech.
        return ScreenOcrChromeFilter.IsLikelyChrome(result.Value)
            ? Result.Fail<string>("Capture rejected: screen shows only window/meeting UI chrome, no task content.")
            : result;
    }
}
