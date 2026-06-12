using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Creates a NEW card answering an interviewer follow-up about a captured screen task,
/// using the captured OCR, all accumulated additions, and the parent card's prior answer.</summary>
/// <param name="SessionId">The active session.</param>
/// <param name="ParentTurnId">The most recent card in the lineage (source of the prior answer).</param>
/// <param name="TopicLabel">The captured-task topic label (used as the new card's title).</param>
/// <param name="Ocr">Combined OCR of the captured task.</param>
/// <param name="Mode">The screen analysis mode the capture used.</param>
/// <param name="Additions">Accumulated interviewer additions (oldest → newest).</param>
/// <param name="RecentTranscript">Recent transcript lines for context.</param>
public sealed record GenerateScreenFollowUpCommand(
    SessionId SessionId,
    ConversationTurnId ParentTurnId,
    string TopicLabel,
    string Ocr,
    ScreenAnalysisMode Mode,
    IReadOnlyList<string> Additions,
    IReadOnlyList<string> RecentTranscript) : IRequest<Result>;

/// <summary>Handles <see cref="GenerateScreenFollowUpCommand"/>.</summary>
public sealed class GenerateScreenFollowUpHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IConversationTurnSink turnSink,
    IUnitOfWork unitOfWork,
    ScreenTaskContextStore screenStore,
    TimeProvider clock) : IRequestHandler<GenerateScreenFollowUpCommand, Result>
{
    private const int PriorAnswerCap = 1200;

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(GenerateScreenFollowUpCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(request.TopicLabel, QuestionSource.Ocr, now);
        session.AddDetectedQuestion(question);
        var turnResult = session.AddConversationTurn(question.Id, request.TopicLabel, now);
        if (turnResult.IsFailed) return Result.Fail(turnResult.Error);
        var turn = turnResult.Value;

        // Persist + notify before streaming so a failed create never leaves a phantom card.
        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return save;
        turnSink.OnTurnCreated(turn.Id, request.TopicLabel);

        // Best-effort prior answer from the parent card (only completed answers are stored).
        var parent = session.ConversationTurns.FirstOrDefault(t => t.Id == request.ParentTurnId);
        var priorText = parent?.AnswerVersions
            .OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.Text;
        var priorAnswer = string.IsNullOrWhiteSpace(priorText)
            ? null
            : priorText!.Length > PriorAnswerCap ? priorText[..PriorAnswerCap] + "…" : priorText;

        var start = session.StartAnswer(turn.InitialQuestionId, now);
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;
        turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

        var prompt = PromptBuilderService.BuildScreenFollowUp(
            session.CodeProfile, session.AnswerSettings,
            request.Ocr, request.Mode, request.Additions, request.RecentTranscript, priorAnswer);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(turn.Id, AnswerVersionType.ScreenFollowUp, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());
            turn.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.ScreenFollowUp, chunks.ToString(), clock.GetUtcNow()));
            turn.TransitionTo(ConversationTurnStatus.RefinedReady);
            await streamSink.OnCompleteAsync(turn.Id, AnswerVersionType.ScreenFollowUp, cancellationToken);
            screenStore.SetLatestCard(turn.Id);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(turn.Id, AnswerErrorMessage.ForUser(ex), cancellationToken);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
