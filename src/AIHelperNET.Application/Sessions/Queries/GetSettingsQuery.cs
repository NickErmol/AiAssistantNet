using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>Query to load the current application settings.</summary>
public sealed record GetSettingsQuery : IRequest<Result<AppSettingsDto>>;

/// <summary>Handles <see cref="GetSettingsQuery"/>.</summary>
public sealed class GetSettingsHandler(ISettingsStore settingsStore)
    : IRequestHandler<GetSettingsQuery, Result<AppSettingsDto>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<AppSettingsDto>> Handle(GetSettingsQuery query, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        return Result.Ok(settings);
    }
}
