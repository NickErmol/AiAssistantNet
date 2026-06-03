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
    ITranscriptSink transcriptSink)
{
    private readonly QuestionDetector _detector = new();

    /// <summary>Processes a single transcript item against the active session.</summary>
    /// <param name="session">The active session to update.</param>
    /// <param name="item">The transcript item to process.</param>
    /// <param name="ct">Cancellation token.</param>
    public Task ProcessAsync(Session session, TranscriptItem item, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        var activeTurn = session.ActiveTurn;
        var recentTexts = session.Questions.Select(q => q.Text).ToList();

        if (item.Speaker == Speaker.Other)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (!detection.IsQuestion) return Task.CompletedTask;

            if (activeTurn is null)
            {
                var q = DetectedQuestion.Create(item.Text, QuestionSource.Audio, item.Timestamp);
                session.AddDetectedQuestion(q);
                var turnResult = session.AddConversationTurn(q.Id, item.Text, item.Timestamp);
                if (turnResult.IsFailed) return Task.CompletedTask;
                var turn = turnResult.Value;

                FireAndForget(new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary), ct);
            }
            else if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
            {
                activeTurn.AttachClarificationResponse(item.Id);
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);

                FireAndForget(new GenerateAnswerCommand(
                    session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification), ct);
            }
        }
        else if (item.Speaker == Speaker.Me &&
                 activeTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (!detection.IsQuestion) return Task.CompletedTask;

            activeTurn.AttachClarificationQuestion(item.Id);
            activeTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
        }

        return Task.CompletedTask;
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(command, ct);
        }, ct);
    }
}
