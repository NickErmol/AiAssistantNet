using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for persisting and retrieving <see cref="SessionReview"/> entities.</summary>
public interface ISessionReviewRepository
{
    /// <summary>Returns the review for a session, or <see langword="null"/> if none exists.</summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SessionReview?> GetBySessionAsync(SessionId sessionId, CancellationToken ct);

    /// <summary>Adds a new review to the store.</summary>
    /// <param name="review">The review to persist.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddAsync(SessionReview review, CancellationToken ct);

    /// <summary>Marks a review as modified so its changes are tracked.</summary>
    /// <param name="review">The modified review.</param>
    void Update(SessionReview review);
}
