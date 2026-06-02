using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>Query to fetch the full projection of a session.</summary>
/// <param name="Id">Session identifier.</param>
public sealed record GetCurrentSessionQuery(SessionId Id) : IRequest<Result<SessionDto>>;

/// <summary>Handles <see cref="GetCurrentSessionQuery"/>.</summary>
public sealed class GetCurrentSessionHandler(ISessionRepository repository)
    : IRequestHandler<GetCurrentSessionQuery, Result<SessionDto>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<SessionDto>> Handle(GetCurrentSessionQuery query, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(query.Id, cancellationToken);
        return get.IsSuccess ? Result.Ok(SessionMapper.ToDto(get.Value)) : get.ToResult<SessionDto>();
    }
}
