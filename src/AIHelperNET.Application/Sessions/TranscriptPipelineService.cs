using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Ids;
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
    ILogger<TranscriptPipelineService>? logger = null,
    IQuestionBoundaryClassifier? boundaryClassifier = null) : IDisposable
{
    private readonly QuestionDetector _detector = new();
    private readonly SegmentAccumulator _accumulator = new();
    private readonly QuestionBoundaryDetector _boundaryDetector = new();
    private readonly ConcurrentDictionary<ConversationTurnId, CancellationTokenSource> _turnCts = new();
    private DateTimeOffset? _collectionStartedAt;
    private const int MaxCollectionSeconds = 8;
    private readonly List<TranscriptItem> _recentItems = [];
    private const int MaxRecentItems = 5;

    /// <summary>Processes a single transcript item against the active session.</summary>
    public async Task ProcessAsync(Session session, TranscriptItem item, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        // Keep recent items for AI classifier context
        _recentItems.Add(item);
        if (_recentItems.Count > MaxRecentItems)
            _recentItems.RemoveAt(0);

        GenerateAnswerCommand? pendingCommand = null;

        if (boundaryClassifier is not null)
        {
            // New 9-state boundary detection path
            pendingCommand = await BuildCommandWithBoundaryAsync(session, item, ct);
        }
        else
        {
            // Legacy 3-state path — preserved for backward compatibility
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
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (pendingCommand is not null)
            FireAndForget(pendingCommand, ct);
    }

    /// <summary>
    /// Drains the accumulator buffer for the legacy 3-state path — call on session stop to process any
    /// remaining buffered segments. When <see cref="IQuestionBoundaryClassifier"/> is configured (the
    /// production default), the accumulator is not used and this method is a no-op.
    /// </summary>
    public async Task FlushAccumulatorAsync(Session session, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var combined = _accumulator.Flush();
        if (combined is null) return;

        var cmd = await BuildCommandForCombinedAsync(session, combined, DateTimeOffset.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (cmd is not null)
            FireAndForget(cmd, ct);
    }

    private async Task<GenerateAnswerCommand?> BuildCommandWithBoundaryAsync(
        Session session, TranscriptItem item, CancellationToken ct)
    {
        // Force-complete if collecting for too long
        var activeTurn = session.ActiveTurn;
        if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion &&
            _collectionStartedAt.HasValue &&
            (DateTimeOffset.UtcNow - _collectionStartedAt.Value).TotalSeconds > MaxCollectionSeconds)
        {
            return ForceCompleteCollection(session, activeTurn);
        }

        var recentTexts = session.Questions.Select(q => q.Text).ToList();
        var activeTurnStatus = activeTurn?.Status;

        // Heuristic first
        var result = _boundaryDetector.Evaluate(item.Text, item.Speaker, activeTurnStatus, recentTexts);

        // If ambiguous (confidence < 0.7), call AI classifier
        if (result.Confidence < 0.7)
        {
            try
            {
                result = await boundaryClassifier!.ClassifyAsync(
                    activeTurnStatus, _recentItems.AsReadOnly(), item, item.Speaker, ct);
            }
            catch (Exception ex)
            {
                if (logger is not null)
                    Log.BoundaryClassifierFailed(logger, ex, item.Text[..Math.Min(80, item.Text.Length)]);
            }

            // Safety net: Rule 4 forces every Speaker.Me utterance that lands during an active turn
            // through the AI at low confidence, and the pipeline's turn status is stale (the answer
            // handler runs in a separate DI scope). When neither the heuristic nor the AI produced a
            // confident, actionable result — the AI threw, or returned NoQuestion/Ambiguous, or a
            // low-confidence clarification — a clearly-formed question would be silently dropped or
            // mis-folded. Re-check with the bias-free heuristic (no active-turn context) and, if it
            // recognises a complete question or task, open a new turn instead of losing it.
            if (result.Confidence < 0.7
                && activeTurnStatus != ConversationTurnStatus.CollectingQuestion)
            {
                var neutral = _boundaryDetector.Evaluate(item.Text, item.Speaker, null, recentTexts);
                if (neutral.Classification is BoundaryLabel.QuestionComplete
                                           or BoundaryLabel.TaskComplete
                                           or BoundaryLabel.NewQuestion)
                {
                    result = neutral;
                }
            }
        }

        item.SetBoundaryRole(LabelToRole(result.Classification));

        if (logger is not null)
            Log.BoundaryRouted(logger, item.Speaker, activeTurnStatus, result.Classification,
                result.Confidence, item.Text[..Math.Min(60, item.Text.Length)]);

        return RouteLabel(session, item, result, activeTurn);
    }

    private GenerateAnswerCommand? RouteLabel(
        Session session, TranscriptItem item, BoundaryClassificationResult result, ConversationTurn? activeTurn)
    {
        switch (result.Classification)
        {
            case BoundaryLabel.QuestionStarted:
                return HandleQuestionStarted(session, item);

            case BoundaryLabel.QuestionContinued:
                // Only a turn that is still being collected can absorb a continuation fragment.
                // Once a question is complete (the in-memory turn is stuck at Detected because the
                // answer handler runs in a separate scope), a "continuation" is really a new
                // question — otherwise AddFragment fails and the segment is silently dropped.
                if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
                {
                    activeTurn.AddFragment(item.Text);
                    return null;
                }
                return HandleNewQuestion(session, item.Text, item.Timestamp);

            case BoundaryLabel.QuestionComplete:
            case BoundaryLabel.TaskComplete:
                return HandleQuestionComplete(session, item, activeTurn);

            case BoundaryLabel.AdditionalRequirement:
                return HandleAdditionalRequirement(session, item, activeTurn);

            case BoundaryLabel.ClarificationOfCurrentQuestion:
                return HandleClarification(session, item, activeTurn);

            case BoundaryLabel.NewQuestion:
                if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
                    activeTurn.Dismiss();
                return HandleNewQuestion(session, item.Text, item.Timestamp);

            default: // NoQuestion, Unrelated
                return null;
        }
    }

    private GenerateAnswerCommand? HandleQuestionStarted(Session session, TranscriptItem item)
    {
        var q = DetectedQuestion.Create(item.Text, QuestionSource.Audio, item.Timestamp);
        session.AddDetectedQuestion(q);
        var turnResult = session.StartCollectingTurn(q.Id, item.Text, item.Timestamp);
        if (!turnResult.IsSuccess) return null;
        var turn = turnResult.Value;
        _collectionStartedAt = DateTimeOffset.UtcNow;
        turnSink.OnTurnCreated(turn.Id, item.Text);
        return null; // No answer yet — still collecting
    }

    private GenerateAnswerCommand? HandleQuestionComplete(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        _collectionStartedAt = null;

        if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
        {
            // Complete the collected fragments into a full question
            activeTurn.AddFragment(item.Text);
            activeTurn.CompleteQuestion();
            turnSink.OnTurnStatusChanged(activeTurn.Id, ConversationTurnStatus.Detected);
            return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
        }

        // No collecting turn — create new
        return HandleNewQuestion(session, item.Text, item.Timestamp);
    }

    private static GenerateAnswerCommand? HandleAdditionalRequirement(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null) return null;
        activeTurn.AppendToQuestion(item.Text);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }

    private GenerateAnswerCommand? HandleClarification(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null) return null;

        if (item.Speaker == Speaker.Me)
        {
            activeTurn.AttachClarificationQuestion(item.Id);
            if (activeTurn.Status != ConversationTurnStatus.AwaitingClarification)
                activeTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
            return null; // My question — no AI answer
        }
        else
        {
            // An interviewer "clarification" only makes sense while we are actually waiting on
            // one. If the turn is already complete (Detected/answered), this is a new question —
            // not a clarification response — so open a fresh turn instead of refining the old one.
            if (activeTurn.Status is not (ConversationTurnStatus.AwaitingClarification
                                          or ConversationTurnStatus.ClarificationReceived))
            {
                return HandleNewQuestion(session, item.Text, item.Timestamp);
            }

            // Other speaker adds clarification context
            activeTurn.AttachClarificationResponse(item.Id);
            if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);
            return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification);
        }
    }

    private GenerateAnswerCommand? ForceCompleteCollection(Session session, ConversationTurn activeTurn)
    {
        _collectionStartedAt = null;
        activeTurn.CompleteQuestion();
        turnSink.OnTurnStatusChanged(activeTurn.Id, ConversationTurnStatus.Detected);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }

    private static BoundaryRole LabelToRole(BoundaryLabel label) => label switch
    {
        BoundaryLabel.QuestionStarted                => BoundaryRole.QuestionStart,
        BoundaryLabel.QuestionContinued              => BoundaryRole.QuestionMiddle,
        BoundaryLabel.QuestionComplete               => BoundaryRole.QuestionEnd,
        BoundaryLabel.TaskComplete                   => BoundaryRole.QuestionEnd,
        BoundaryLabel.ClarificationOfCurrentQuestion => BoundaryRole.Clarification,
        BoundaryLabel.AdditionalRequirement          => BoundaryRole.AdditionalRequirement,
        BoundaryLabel.NewQuestion                    => BoundaryRole.NewQuestion,
        BoundaryLabel.Unrelated                      => BoundaryRole.Unrelated,
        _                                            => BoundaryRole.None
    };

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

    /// <summary>Releases resources owned by this service.</summary>
    public void Dispose()
    {
        foreach (var cts in _turnCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _turnCts.Clear();
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken sessionCt)
    {
        // Each turn owns its own CTS. Re-firing a turn cancels that turn's prior in-flight
        // generation (same-turn regeneration); distinct turns are independent and never cancel
        // each other.
        var cts = _turnCts.AddOrUpdate(
            command.TurnId,
            _ => new CancellationTokenSource(),
            (_, old) => { old.Cancel(); old.Dispose(); return new CancellationTokenSource(); });
        var requestCt = cts.Token;

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediator    = scope.ServiceProvider.GetRequiredService<IMediator>();
            // Use the per-turn token so a regeneration of THIS turn cancels this generation.
            // Session stop is ignored here intentionally (fire-and-forget completes on its own).
            await mediator.Send(command, requestCt);
        }, CancellationToken.None);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "HaikuClassifier: call failed, falling back to NewQuestion for: {Text}")]
        internal static partial void ClassifierFailed(ILogger logger, Exception ex, string text);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "BoundaryClassifier: call failed, using heuristic result for: {Text}")]
        internal static partial void BoundaryClassifierFailed(ILogger logger, Exception ex, string text);

        [LoggerMessage(Level = LogLevel.Information,
            Message = "BoundaryRoute: speaker={Speaker} staleStatus={Status} -> {Label} ({Confidence:F2}) text='{Text}'")]
        internal static partial void BoundaryRouted(
            ILogger logger, Speaker speaker, ConversationTurnStatus? status, BoundaryLabel label,
            double confidence, string text);
    }
}
