using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Persists the full application settings snapshot.</summary>
public sealed record SaveSettingsCommand(AppSettingsDto Settings) : IRequest<Result>;

/// <summary>Handles <see cref="SaveSettingsCommand"/>.</summary>
public sealed class SaveSettingsHandler(ISettingsStore settingsStore)
    : IRequestHandler<SaveSettingsCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(SaveSettingsCommand request, CancellationToken cancellationToken)
    {
        await settingsStore.SaveAsync(request.Settings.Normalized(), cancellationToken);
        return Result.Ok();
    }
}
