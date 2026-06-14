using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Regenerates an answer for a conversation turn using OCR screen content and a specific analysis mode.</summary>
/// <param name="SessionId">The session containing the turn.</param>
/// <param name="TurnId">The turn to regenerate an answer for.</param>
/// <param name="ScreenContext">The OCR-extracted text from the screen.</param>
/// <param name="Mode">The screen analysis strategy to apply.</param>
/// <param name="InterviewerLines">Recent interviewer speech lines for additional context.</param>
public sealed record RegenerateAnswerWithScreenCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string ScreenContext,
    ScreenAnalysisMode Mode,
    string[] InterviewerLines) : IRequest<Result>;

/// <summary>Handles <see cref="RegenerateAnswerWithScreenCommand"/>.</summary>
public sealed partial class RegenerateAnswerWithScreenHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RegenerateAnswerWithScreenHandler> logger) : IRequestHandler<RegenerateAnswerWithScreenCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(RegenerateAnswerWithScreenCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == request.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

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
                await streamSink.OnChunkAsync(request.TurnId, AnswerVersionType.UpdatedWithScreen, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());

            var version = AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);

            turn.TransitionTo(ConversationTurnStatus.RefinedReady);

            await streamSink.OnCompleteAsync(request.TurnId, AnswerVersionType.UpdatedWithScreen, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            Log.GenerationFailed(logger, ex, request.TurnId.Value);
            await streamSink.OnErrorAsync(request.TurnId, AnswerErrorMessage.ForUser(ex), cancellationToken);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Screen-regenerated answer failed for turn {TurnId}; surfaced friendly error to user")]
        internal static partial void GenerationFailed(ILogger logger, Exception ex, Guid turnId);
    }
}
