using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Creates a new conversation turn from OCR screen content and streams an answer for it.</summary>
/// <param name="SessionId">The active session.</param>
/// <param name="ScreenContext">OCR-extracted text from the screen.</param>
/// <param name="Mode">The screen analysis strategy to apply.</param>
/// <param name="InterviewerLines">Recent interviewer speech lines for additional context.</param>
public sealed record StartScreenTurnCommand(
    SessionId SessionId,
    string ScreenContext,
    ScreenAnalysisMode Mode,
    string[] InterviewerLines) : IRequest<Result>;

/// <summary>Handles <see cref="StartScreenTurnCommand"/>.</summary>
public sealed class StartScreenTurnHandler(
    ISessionRepository repository,
    IConversationTurnSink turnSink,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StartScreenTurnCommand, Result>
{
    private const string QuestionLabel = "[Screen capture]";

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(StartScreenTurnCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(QuestionLabel, QuestionSource.Ocr, now);
        session.AddDetectedQuestion(question);

        var turnResult = session.AddConversationTurn(question.Id, QuestionLabel, now);
        if (turnResult.IsFailed) return Result.Fail(turnResult.Error);
        var turn = turnResult.Value;

        // Notify UI before saving so the turn card exists when the first chunk arrives.
        turnSink.OnTurnCreated(turn.Id, QuestionLabel);

        var start = session.StartAnswer(question.Id, now);
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        // Persist question + turn before streaming so the DB is consistent.
        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return save;

        var prompt = PromptBuilderService.BuildWithScreenMode(
            session.CodeProfile, session.AnswerSettings,
            request.ScreenContext, request.InterviewerLines, request.Mode);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(turn.Id, AnswerVersionType.UpdatedWithScreen, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());

            var version = AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);
            turn.TransitionTo(ConversationTurnStatus.RefinedReady);

            await streamSink.OnCompleteAsync(turn.Id, AnswerVersionType.UpdatedWithScreen, cancellationToken);
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
