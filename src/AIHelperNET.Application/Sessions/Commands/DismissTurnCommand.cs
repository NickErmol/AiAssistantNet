using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to dismiss a conversation turn.</summary>
/// <param name="SessionId">The session containing the turn.</param>
/// <param name="TurnId">The turn to dismiss.</param>
public sealed record DismissTurnCommand(
    SessionId SessionId,
    ConversationTurnId TurnId) : IRequest<Result>;

/// <summary>Handles <see cref="DismissTurnCommand"/>.</summary>
public sealed class DismissTurnHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<DismissTurnCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(DismissTurnCommand request, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();

        var turn = get.Value.ConversationTurns.FirstOrDefault(t => t.Id == request.TurnId);
        if (turn is null) return Result.Fail("Turn not found.");

        turn.Dismiss();
        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
