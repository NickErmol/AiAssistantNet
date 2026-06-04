using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Application.Sessions;

/// <summary>Processes incoming transcript items and drives conversation turn lifecycle.</summary>
public sealed class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink)
{
    private readonly QuestionDetector _detector = new();

    /// <summary>Processes a single transcript item against the active session.</summary>
    /// <param name="session">The active session to update.</param>
    /// <param name="item">The transcript item to process.</param>
    /// <param name="unitOfWork">Unit of work that owns the session — saved before any command is fired.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task ProcessAsync(Session session, TranscriptItem item, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        var activeTurn  = session.ActiveTurn;
        var recentTexts = session.Questions.Select(q => q.Text).ToList();

        GenerateAnswerCommand? pendingCommand = null;

        if (item.Speaker == Speaker.Other)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (detection.IsQuestion)
            {
                if (activeTurn is null)
                {
                    var q          = DetectedQuestion.Create(item.Text, QuestionSource.Audio, item.Timestamp);
                    session.AddDetectedQuestion(q);
                    var turnResult = session.AddConversationTurn(q.Id, item.Text, item.Timestamp);
                    if (turnResult.IsSuccess)
                    {
                        var turn = turnResult.Value;
                        turnSink.OnTurnCreated(turn.Id, item.Text);
                        pendingCommand = new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary);
                    }
                }
                else if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
                {
                    activeTurn.AttachClarificationResponse(item.Id);
                    activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);
                    pendingCommand = new GenerateAnswerCommand(
                        session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification);
                }
            }
        }
        else if (item.Speaker == Speaker.Me &&
                 activeTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (detection.IsQuestion)
            {
                activeTurn.AttachClarificationQuestion(item.Id);
                activeTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
            }
        }

        // Persist all mutations (transcript item, detected question, turn) before firing any command.
        await unitOfWork.SaveChangesAsync(ct);

        if (pendingCommand is not null)
            FireAndForget(pendingCommand, ct);
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            using var scope  = scopeFactory.CreateScope();
            var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(command, ct);
        }, ct);
    }
}
