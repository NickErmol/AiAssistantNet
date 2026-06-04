using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for persisting and retrieving <see cref="Session"/> aggregates.</summary>
public interface ISessionRepository
{
    /// <summary>Retrieves a session by its identifier.</summary>
    /// <param name="id">Session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<Session>> GetAsync(SessionId id, CancellationToken ct);

    /// <summary>Adds a new session to the store.</summary>
    /// <param name="session">The session to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(Session session, CancellationToken ct);

    /// <summary>Returns summary projections for the most recent sessions.</summary>
    /// <param name="take">Maximum number of records to return.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<SessionSummaryDto>> GetHistoryAsync(int take, CancellationToken ct);

    /// <summary>Marks a session as modified so its changes are tracked.</summary>
    /// <param name="session">The modified session.</param>
    void Update(Session session);

    /// <summary>Returns full transcript and answers for a single session.</summary>
    /// <param name="id">Session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SessionDetailDto?> GetDetailAsync(SessionId id, CancellationToken ct);
}
