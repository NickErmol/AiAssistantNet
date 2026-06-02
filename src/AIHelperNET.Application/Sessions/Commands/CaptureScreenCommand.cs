using AIHelperNET.Application.Abstractions;
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
        => await ocrService.CaptureAndReadAsync(cancellationToken);
}
