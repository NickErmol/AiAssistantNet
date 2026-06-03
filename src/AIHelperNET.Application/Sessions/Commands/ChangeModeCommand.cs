using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to change the capture mode of an active session.</summary>
/// <param name="SessionId">The session to update.</param>
/// <param name="Mode">The new session mode.</param>
/// <param name="AudioSource">The new audio source selection.</param>
public sealed record ChangeModeCommand(
    SessionId SessionId,
    SessionMode Mode,
    AudioSourceMode AudioSource) : IRequest<Result>;

/// <summary>Handles <see cref="ChangeModeCommand"/>.</summary>
public sealed class ChangeModeHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<ChangeModeCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(ChangeModeCommand request, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();

        var result = get.Value.ChangeMode(request.Mode, request.AudioSource);
        if (result.IsFailed) return Result.Fail(result.Error);

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
