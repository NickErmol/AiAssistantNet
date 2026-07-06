using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Reviews.Commands;

/// <summary>Generates (or regenerates) the post-session review for the specified session.</summary>
/// <param name="SessionId">The session to review.</param>
public sealed record GenerateSessionReviewCommand(SessionId SessionId)
    : IRequest<Result<SessionReviewDto>>;

/// <summary>Handles <see cref="GenerateSessionReviewCommand"/>.</summary>
public sealed class GenerateSessionReviewHandler(
    ISessionRepository sessionRepository,
    ISessionReviewRepository reviewRepository,
    ISessionReviewAnalyzer analyzer,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateSessionReviewCommand, Result<SessionReviewDto>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<SessionReviewDto>> Handle(
        GenerateSessionReviewCommand command, CancellationToken cancellationToken)
    {
        // 1. Load session
        var getSession = await sessionRepository.GetAsync(command.SessionId, cancellationToken);
        if (getSession.IsFailed) return getSession.ToResult();

        var session = getSession.Value;

        // 2. Guard: no transcript → fail without calling the analyzer
        if (session.Transcript.Count == 0)
            return Result.Fail("Session has no transcript to review.");

        // 3. Build prompt
        var prompt = SessionReviewPromptBuilder.Build(session);

        // 4. Analyze — propagate failure without saving
        var analyzeResult = await analyzer.AnalyzeAsync(prompt, cancellationToken);
        if (analyzeResult.IsFailed) return analyzeResult.ToResult();

        var reviewResult = analyzeResult.Value;
        var now = clock.GetUtcNow();

        // 5. Upsert
        var existing = await reviewRepository.GetBySessionAsync(command.SessionId, cancellationToken);
        if (existing is not null)
        {
            existing.Replace(reviewResult.Markdown, reviewResult.ModelUsed, now);
            reviewRepository.Update(existing);
        }
        else
        {
            var newReview = SessionReview.Create(
                command.SessionId, reviewResult.Markdown, reviewResult.ModelUsed, now);
            await reviewRepository.AddAsync(newReview, cancellationToken);
        }

        // 6. Save
        var saveResult = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saveResult.IsFailed) return Result.Fail<SessionReviewDto>(saveResult.Errors);

        return Result.Ok(new SessionReviewDto(reviewResult.Markdown, reviewResult.ModelUsed, now));
    }
}
