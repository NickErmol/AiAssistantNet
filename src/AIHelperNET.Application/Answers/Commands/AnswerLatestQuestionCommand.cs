using System.Globalization;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>
/// Manual "answer the latest question" command (Ctrl+Shift+Z): derives the most recent question from
/// the configured transcript look-back window plus the supplied recent captures, then answers it as a
/// normal turn. A recovery path for questions the live pipeline missed.
/// </summary>
/// <param name="SessionId">The active session.</param>
/// <param name="RecentCaptures">The most-recent screen captures (≤2), labeled with age.</param>
public sealed record AnswerLatestQuestionCommand(
    SessionId SessionId,
    IReadOnlyList<RecentCapture> RecentCaptures) : IRequest<Result>;

/// <summary>Handles <see cref="AnswerLatestQuestionCommand"/>.</summary>
public sealed class AnswerLatestQuestionHandler(
    ISessionRepository repository,
    ISettingsStore settingsStore,
    ILatestQuestionExtractor extractor,
    IConversationTurnSink turnSink,
    IAnswerStreamSink streamSink,
    IMediator mediator,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<AnswerLatestQuestionCommand, Result>
{
    private const string NoQuestionLabel = "[No recent question found]";

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(AnswerLatestQuestionCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var windowSeconds = settings.LatestQuestionWindowSeconds;

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var now = clock.GetUtcNow();
        var cutoff = now - TimeSpan.FromSeconds(windowSeconds);
        var window = session.Transcript
            .Where(t => t.Timestamp >= cutoff)
            .OrderBy(t => t.Timestamp)
            .Select(t => new TranscriptLine(
                t.Speaker == Speaker.Other ? "Interviewer" : "Candidate", t.Text))
            .ToList();

        var screenContext = BuildScreenContext(request.RecentCaptures);

        if (window.Count == 0)
            return await ReportNoQuestionAsync(session, windowSeconds, cancellationToken);

        var extracted = await extractor.ExtractAsync(window, screenContext, cancellationToken);
        if (!extracted.Found || string.IsNullOrWhiteSpace(extracted.QuestionText))
            return await ReportNoQuestionAsync(session, windowSeconds, cancellationToken);

        var createResult = await CreateTurnAsync(session, extracted.QuestionText, cancellationToken);
        if (createResult.IsFailed) return createResult.ToResult();
        var turnId = createResult.Value;

        return await mediator.Send(
            new GenerateAnswerCommand(request.SessionId, turnId, AnswerVersionType.Preliminary, screenContext),
            cancellationToken);
    }

    private async ValueTask<Result> ReportNoQuestionAsync(Session session, int windowSeconds, CancellationToken cancellationToken)
    {
        var createResult = await CreateTurnAsync(session, NoQuestionLabel, cancellationToken);
        if (createResult.IsFailed) return createResult.ToResult();
        await streamSink.OnErrorAsync(createResult.Value,
            $"No question found in the last {windowSeconds}s. Increase the window in Settings, " +
            "or capture the screen first if it's a coding task.", cancellationToken);
        return Result.Ok();
    }

    /// <summary>Creates + persists a turn for <paramref name="questionText"/> (same path as screen
    /// turns) and announces it to the UI only after a successful save.</summary>
    private async ValueTask<Result<ConversationTurnId>> CreateTurnAsync(
        Session session, string questionText, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(questionText, QuestionSource.Audio, now);
        session.AddDetectedQuestion(question);

        var turnResult = session.AddConversationTurn(question.Id, questionText, now);
        if (turnResult.IsFailed) return Result.Fail<ConversationTurnId>(turnResult.Error);
        var turn = turnResult.Value;

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return Result.Fail<ConversationTurnId>(save.Errors);

        turnSink.OnTurnCreated(turn.Id, questionText);
        return Result.Ok(turn.Id);
    }

    private static string? BuildScreenContext(IReadOnlyList<RecentCapture> captures)
    {
        if (captures.Count == 0) return null;
        var sb = new StringBuilder();
        for (var i = 0; i < captures.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            sb.Append(CultureInfo.InvariantCulture,
                $"--- Screen capture ({captures[i].AgeLabel}) ---\n{captures[i].Ocr}");
        }
        return sb.ToString();
    }
}
