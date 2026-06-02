using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to stop an active session.</summary>
/// <param name="SessionId">Identifier of the session to stop.</param>
public sealed record StopSessionCommand(SessionId SessionId) : IRequest<Result>;

/// <summary>Handles <see cref="StopSessionCommand"/>.</summary>
public sealed class StopSessionHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StopSessionCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(StopSessionCommand command, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(command.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();

        var stop = get.Value.Stop(clock.GetUtcNow());
        if (stop.IsFailed) return Result.Fail(stop.Error);

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
