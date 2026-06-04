using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Persists a new answer-panel font size to the settings store.</summary>
/// <param name="FontSize">Desired font size; clamped to [<see cref="SaveAnswerFontSizeHandler.Min"/>, <see cref="SaveAnswerFontSizeHandler.Max"/>].</param>
public sealed record SaveAnswerFontSizeCommand(int FontSize) : IRequest<Result>;

/// <summary>Handles <see cref="SaveAnswerFontSizeCommand"/>.</summary>
public sealed class SaveAnswerFontSizeHandler(ISettingsStore settingsStore)
    : IRequestHandler<SaveAnswerFontSizeCommand, Result>
{
    /// <summary>Minimum allowed answer font size (pt).</summary>
    public const int Min = 9;

    /// <summary>Maximum allowed answer font size (pt).</summary>
    public const int Max = 20;

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(SaveAnswerFontSizeCommand command, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(command.FontSize, Min, Max);
        var current = await settingsStore.LoadAsync(cancellationToken);
        await settingsStore.SaveAsync(current with { AnswerFontSize = clamped }, cancellationToken);
        return Result.Ok();
    }
}
