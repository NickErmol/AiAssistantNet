using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Creates an empty "[Screen capture]" conversation turn and returns its id (no streaming).</summary>
/// <param name="SessionId">The active session.</param>
public sealed record CreateScreenTurnCommand(SessionId SessionId) : IRequest<Result<ConversationTurnId>>;

/// <summary>Handles <see cref="CreateScreenTurnCommand"/>.</summary>
public sealed class CreateScreenTurnHandler(
    ISessionRepository repository,
    IConversationTurnSink turnSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CreateScreenTurnCommand, Result<ConversationTurnId>>
{
    private const string QuestionLabel = "[Screen capture]";

    /// <inheritdoc/>
    public async ValueTask<Result<ConversationTurnId>> Handle(CreateScreenTurnCommand request, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return Result.Fail<ConversationTurnId>(get.Errors);
        var session = get.Value;

        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(QuestionLabel, QuestionSource.Ocr, now);
        session.AddDetectedQuestion(question);

        var turnResult = session.AddConversationTurn(question.Id, QuestionLabel, now);
        if (turnResult.IsFailed) return Result.Fail<ConversationTurnId>(turnResult.Error);
        var turn = turnResult.Value;

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return Result.Fail<ConversationTurnId>(save.Errors);

        // Notify the UI only after a successful save, so a cancelled or failed create never
        // leaves a phantom card. The streaming answer (RegenerateAnswerWithScreen) runs afterwards.
        turnSink.OnTurnCreated(turn.Id, QuestionLabel);
        return Result.Ok(turn.Id);
    }
}
