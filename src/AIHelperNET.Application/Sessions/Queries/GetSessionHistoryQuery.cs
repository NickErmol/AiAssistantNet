using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>Query to retrieve a list of recent session summaries.</summary>
/// <param name="Take">Maximum number of records to return.</param>
public sealed record GetSessionHistoryQuery(int Take = 50) : IRequest<Result<IReadOnlyList<SessionSummaryDto>>>;

/// <summary>Handles <see cref="GetSessionHistoryQuery"/>.</summary>
public sealed class GetSessionHistoryHandler(ISessionRepository repository)
    : IRequestHandler<GetSessionHistoryQuery, Result<IReadOnlyList<SessionSummaryDto>>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<IReadOnlyList<SessionSummaryDto>>> Handle(
        GetSessionHistoryQuery query, CancellationToken cancellationToken)
    {
        var history = await repository.GetHistoryAsync(query.Take, cancellationToken);
        return Result.Ok(history);
    }
}
