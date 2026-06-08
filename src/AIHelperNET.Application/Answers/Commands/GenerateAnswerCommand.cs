using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Command to generate an AI answer for a conversation turn.</summary>
/// <param name="SessionId">Session containing the turn.</param>
/// <param name="TurnId">The conversation turn to answer.</param>
/// <param name="VersionType">Which version type this answer represents.</param>
/// <param name="ScreenContext">Optional OCR context captured from the screen.</param>
public sealed record GenerateAnswerCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    AnswerVersionType VersionType = AnswerVersionType.Preliminary,
    string? ScreenContext = null) : IRequest<Result>;

/// <summary>Handles <see cref="GenerateAnswerCommand"/>.</summary>
public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ITurnStatusFeedback feedback) : IRequestHandler<GenerateAnswerCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(GenerateAnswerCommand cmd, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(cmd.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var question = session.Questions.FirstOrDefault(q => q.Id == turn.InitialQuestionId);
        if (question is null) return Result.Fail("Question not found.");

        // For multi-fragment collected questions, InitialQuestionText contains the fully
        // assembled text (all fragments joined). Fall back to question.Text only when the
        // assembled text is absent (single-fragment / legacy turns).
        var questionText = string.IsNullOrWhiteSpace(turn.InitialQuestionText)
            ? question.Text
            : turn.InitialQuestionText;

        // Collect last 5 transcript items up to (and including) when this turn was created.
        var recentTranscript = session.Transcript
            .Where(t => t.Timestamp <= turn.CreatedAt)
            .TakeLast(5)
            .ToList();

        // Collect last 2 completed turns (excluding the current one) with at least one answer version.
        var recentQA = session.ConversationTurns
            .Where(t => t.Id != cmd.TurnId
                     && t.AnswerVersions.Count > 0
                     && (t.Status == ConversationTurnStatus.PreliminaryReady
                         || t.Status == ConversationTurnStatus.RefinedReady
                         || t.Status == ConversationTurnStatus.Resolved))
            .TakeLast(2)
            .Select(t =>
            {
                var ans = t.AnswerVersions[^1].Text;
                return (Question: t.InitialQuestionText ?? "(unknown question)",
                        Answer: ans);
            })
            .ToList();

        var genStatus = cmd.VersionType == AnswerVersionType.Preliminary
            ? ConversationTurnStatus.GeneratingPreliminary
            : ConversationTurnStatus.GeneratingRefined;
        turn.TransitionTo(genStatus);
        feedback.Publish(new TurnStatusEvent(cmd.TurnId, genStatus));

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = PromptBuilderService.Build(
            session.CodeProfile, session.AnswerSettings, questionText,
            cmd.ScreenContext,
            recentTranscript,
            recentQA);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(cmd.TurnId, cmd.VersionType, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());

            var version = AnswerVersion.Create(cmd.VersionType, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);

            var readyStatus = cmd.VersionType == AnswerVersionType.Preliminary
                ? ConversationTurnStatus.PreliminaryReady
                : ConversationTurnStatus.RefinedReady;
            turn.TransitionTo(readyStatus);
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, readyStatus));

            await streamSink.OnCompleteAsync(cmd.TurnId, cmd.VersionType, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, turn.Status));
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, turn.Status));
            await streamSink.OnErrorAsync(cmd.TurnId, ex.Message, cancellationToken);
        }
#pragma warning restore CA1031

        // No repository.Update(session): rely on EF change-tracking so only the entities this
        // handler actually modified (turn Status, the new AnswerVersion, the GeneratedAnswer) are
        // written. A full-graph Update would mark pipeline-owned columns (clarification IDs,
        // pre-answer status) Modified and clobber them. See Spec 1 §5.5.
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
