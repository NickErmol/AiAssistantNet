using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.ValueObjects;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to update answer settings and/or code profile on a session.</summary>
/// <param name="SessionId">Session to update.</param>
/// <param name="AnswerSettings">New answer settings, or null to leave unchanged.</param>
/// <param name="CodeProfile">New code profile, or null to leave unchanged.</param>
public sealed record UpdateSettingsCommand(
    SessionId SessionId,
    AnswerSettings? AnswerSettings,
    CodeProfile? CodeProfile) : IRequest<Result>;

/// <summary>Handles <see cref="UpdateSettingsCommand"/>.</summary>
public sealed class UpdateSettingsHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSettingsCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(UpdateSettingsCommand command, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(command.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();

        if (command.AnswerSettings is not null)
            get.Value.UpdateAnswerSettings(command.AnswerSettings);
        if (command.CodeProfile is not null)
            get.Value.UpdateCodeProfile(command.CodeProfile);

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
