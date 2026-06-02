using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to start a new interview session.</summary>
/// <param name="AnswerSettings">Initial answer settings for the session.</param>
/// <param name="CodeProfile">Candidate's code profile.</param>
public sealed record StartSessionCommand(
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile) : IRequest<Result<SessionDto>>;

/// <summary>Handles <see cref="StartSessionCommand"/>.</summary>
public sealed class StartSessionHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StartSessionCommand, Result<SessionDto>>
{
    /// <inheritdoc/>
    public async ValueTask<Result<SessionDto>> Handle(
        StartSessionCommand command, CancellationToken cancellationToken)
    {
        var create = Session.Create(command.AnswerSettings, command.CodeProfile, clock.GetUtcNow());
        if (create.IsFailed)
            return Result.Fail(create.Error);

        var session = create.Value;
        await repository.AddAsync(session, cancellationToken);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return save;

        return Result.Ok(SessionMapper.ToDto(session));
    }
}
