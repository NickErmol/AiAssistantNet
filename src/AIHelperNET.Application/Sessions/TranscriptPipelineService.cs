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
    ILogger<TranscriptPipelineService>? logger = null,
    IQuestionBoundaryClassifier? boundaryClassifier = null) : IDisposable
{
    private readonly QuestionDetector _detector = new();
    private readonly SegmentAccumulator _accumulator = new();
    private readonly QuestionBoundaryDetector _boundaryDetector = new();
    private CancellationTokenSource? _currentAnswerCts;
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
        }

        item.SetBoundaryRole(LabelToRole(result.Classification));

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
                if (activeTurn is not null)
                    activeTurn.AddFragment(item.Text);
                return null;

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
        var old = _currentAnswerCts;
        _currentAnswerCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();

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

    private GenerateAnswerCommand? HandleAdditionalRequirement(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null) return null;
        var old = _currentAnswerCts;
        _currentAnswerCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
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
            // Other speaker adds clarification context
            activeTurn.AttachClarificationResponse(item.Id);
            if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);
            var old = _currentAnswerCts;
            _currentAnswerCts = new CancellationTokenSource();
            old?.Cancel();
            old?.Dispose();
            return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification);
        }
    }

    private GenerateAnswerCommand? ForceCompleteCollection(Session session, ConversationTurn activeTurn)
    {
        _collectionStartedAt = null;
        var old = _currentAnswerCts;
        _currentAnswerCts = new CancellationTokenSource();
        old?.Cancel();
        old?.Dispose();
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
        _currentAnswerCts?.Cancel();
        _currentAnswerCts?.Dispose();
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken sessionCt)
    {
        var requestCt = _currentAnswerCts?.Token ?? CancellationToken.None;
        _ = Task.Run(async () =>
        {
            using var scope  = scopeFactory.CreateScope();
            var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
            // Use the per-request token so new context cancels this generation.
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
    }
}
