using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIHelperNET.Application.Sessions;

/// <summary>Processes incoming transcript items and drives conversation turn lifecycle.</summary>
public sealed partial class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink,
    IQuestionClassifier classifier,
    ILogger<TranscriptPipelineService>? logger = null)
{
    private readonly QuestionDetector _detector = new();
    private readonly SegmentAccumulator _accumulator = new();

    /// <summary>Processes a single transcript item against the active session.</summary>
    public async Task ProcessAsync(Session session, TranscriptItem item, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        GenerateAnswerCommand? pendingCommand = null;

        if (item.Speaker == Speaker.Other)
        {
            var combined = _accumulator.Add(item.Text, item.Timestamp);
            if (combined is not null)
                pendingCommand = await BuildCommandForCombinedAsync(session, combined, item.Timestamp, ct);
        }
        else if (item.Speaker == Speaker.Me &&
                 session.ActiveTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = _detector.Evaluate(item.Text, session.Questions.Select(q => q.Text).ToList());
            if (detection.IsQuestion)
            {
                session.ActiveTurn.AttachClarificationQuestion(item.Id);
                session.ActiveTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (pendingCommand is not null)
            FireAndForget(pendingCommand, ct);
    }

    /// <summary>Drains the accumulator buffer — call on session stop to process any remaining buffered segments.</summary>
    public async Task FlushAccumulatorAsync(Session session, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var combined = _accumulator.Flush();
        if (combined is null) return;

        var cmd = await BuildCommandForCombinedAsync(session, combined, DateTimeOffset.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (cmd is not null)
            FireAndForget(cmd, ct);
    }

    private async Task<GenerateAnswerCommand?> BuildCommandForCombinedAsync(
        Session session, string combined, DateTimeOffset timestamp, CancellationToken ct)
    {
        var recentTexts = session.Questions.Select(q => q.Text).ToList();

        // Pre-filter: quick heuristic check before making a Haiku API call.
        // We pass the segment through if it looks like a question, or if there is already an
        // active turn (the segment might be a continuation fragment that won't start with an
        // interrogative). Short/noise texts are still blocked via IsDuplicate or the MinWords
        // check embedded in the detector (IsQuestion = false AND no active turn).
        var preFilter = _detector.Evaluate(combined, recentTexts);
        var hasActiveTurn = session.ActiveTurn is not null;
        if (preFilter.IsDuplicate || (!preFilter.IsQuestion && !hasActiveTurn))
            return null;

        // LLM classification.
        ClassificationResult classification;
        try
        {
            classification = await classifier.ClassifyAsync(
                combined, recentTexts.TakeLast(2).ToList(), ct);
        }
        catch (Exception ex)
        {
            if (logger is not null)
                Log.ClassifierFailed(logger, ex, combined[..Math.Min(80, combined.Length)]);
            classification = ClassificationResult.NewQuestion;
        }

        return classification switch
        {
            ClassificationResult.NewQuestion  => HandleNewQuestion(session, combined, timestamp),
            ClassificationResult.Continuation => HandleContinuation(session, combined),
            _                                 => null,
        };
    }

    private GenerateAnswerCommand? HandleNewQuestion(Session session, string combined, DateTimeOffset timestamp)
    {
        var q = DetectedQuestion.Create(combined, QuestionSource.Audio, timestamp);
        session.AddDetectedQuestion(q);
        var turnResult = session.AddConversationTurn(q.Id, combined, timestamp);
        if (!turnResult.IsSuccess) return null;

        var turn = turnResult.Value;
        turnSink.OnTurnCreated(turn.Id, combined);
        return new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary);
    }

    private GenerateAnswerCommand? HandleContinuation(Session session, string combined)
    {
        var activeTurn = session.ActiveTurn;

        if (activeTurn is null
            || activeTurn.Status == ConversationTurnStatus.AwaitingClarification
            || activeTurn.Status == ConversationTurnStatus.ClarificationReceived
            || activeTurn.Status == ConversationTurnStatus.GeneratingRefined
            || activeTurn.Status == ConversationTurnStatus.RefinedReady
            || activeTurn.Status == ConversationTurnStatus.Dismissed
            || activeTurn.Status == ConversationTurnStatus.Resolved)
        {
            // No open turn or post-clarification turn — promote to NewQuestion.
            return HandleNewQuestion(session, combined, DateTimeOffset.UtcNow);
        }

        activeTurn.AppendToQuestion(combined);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            using var scope  = scopeFactory.CreateScope();
            var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(command, CancellationToken.None);
        }, CancellationToken.None);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "HaikuClassifier: call failed, falling back to NewQuestion for: {Text}")]
        internal static partial void ClassifierFailed(ILogger logger, Exception ex, string text);
    }
}
