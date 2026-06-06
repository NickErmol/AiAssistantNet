using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>Query to retrieve the full transcript and answers for a single session.</summary>
/// <param name="Id">The session identifier.</param>
public sealed record GetSessionDetailQuery(SessionId Id) : IRequest<Result<SessionDetailDto?>>;

/// <summary>Handles <see cref="GetSessionDetailQuery"/>.</summary>
public sealed class GetSessionDetailHandler(ISessionRepository repository)
    : IRequestHandler<GetSessionDetailQuery, Result<SessionDetailDto?>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<SessionDetailDto?>> Handle(
        GetSessionDetailQuery query, CancellationToken cancellationToken)
    {
        var detail = await repository.GetDetailAsync(query.Id, cancellationToken);
        return Result.Ok(detail);
    }
}
