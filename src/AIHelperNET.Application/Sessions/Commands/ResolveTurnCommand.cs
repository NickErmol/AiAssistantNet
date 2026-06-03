using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to mark a conversation turn as resolved.</summary>
/// <param name="SessionId">The session containing the turn.</param>
/// <param name="TurnId">The turn to resolve.</param>
public sealed record ResolveTurnCommand(
    SessionId SessionId,
    ConversationTurnId TurnId) : IRequest<Result>;

/// <summary>Handles <see cref="ResolveTurnCommand"/>.</summary>
public sealed class ResolveTurnHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<ResolveTurnCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(ResolveTurnCommand request, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();

        var turn = get.Value.ConversationTurns.FirstOrDefault(t => t.Id == request.TurnId);
        if (turn is null) return Result.Fail("Turn not found.");

        turn.Resolve();
        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
