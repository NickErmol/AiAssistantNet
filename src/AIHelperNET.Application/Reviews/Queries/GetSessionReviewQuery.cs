using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Reviews.Queries;

/// <summary>Returns the stored review for the given session, or <see langword="null"/> if none exists.</summary>
/// <param name="SessionId">The session to look up.</param>
public sealed record GetSessionReviewQuery(SessionId SessionId)
    : IRequest<Result<SessionReviewDto?>>;

/// <summary>Handles <see cref="GetSessionReviewQuery"/>.</summary>
public sealed class GetSessionReviewHandler(ISessionReviewRepository reviewRepository)
    : IRequestHandler<GetSessionReviewQuery, Result<SessionReviewDto?>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<SessionReviewDto?>> Handle(
        GetSessionReviewQuery query, CancellationToken cancellationToken)
    {
        var review = await reviewRepository.GetBySessionAsync(query.SessionId, cancellationToken);
        if (review is null) return Result.Ok<SessionReviewDto?>(null);

        return Result.Ok<SessionReviewDto?>(
            new SessionReviewDto(review.Markdown, review.ModelUsed, review.GeneratedAt));
    }
}
