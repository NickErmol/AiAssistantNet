using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Generates a follow-up answer using the original question and prior answer as context.</summary>
/// <param name="SessionId">The session containing the turn.</param>
/// <param name="TurnId">The turn to generate a follow-up for.</param>
/// <param name="FollowUpText">The user-supplied follow-up question or clarification.</param>
public sealed record GenerateFollowUpCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string FollowUpText) : IRequest<Result>;

/// <summary>Handles <see cref="GenerateFollowUpCommand"/>.</summary>
public sealed class GenerateFollowUpHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateFollowUpCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(GenerateFollowUpCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == request.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var question = session.Questions.FirstOrDefault(q => q.Id == turn.InitialQuestionId);
        if (question is null) return Result.Fail("Question not found.");

        var priorText = turn.AnswerVersions
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault()?.Text ?? string.Empty;

        turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = PromptBuilderService.BuildFollowUp(
            session.CodeProfile, session.AnswerSettings,
            question.Text, priorText, request.FollowUpText);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(request.TurnId, AnswerVersionType.FollowUp, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());

            var version = AnswerVersion.Create(AnswerVersionType.FollowUp, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);

            turn.TransitionTo(ConversationTurnStatus.RefinedReady);

            await streamSink.OnCompleteAsync(request.TurnId, AnswerVersionType.FollowUp, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(request.TurnId, ex.Message, cancellationToken);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
