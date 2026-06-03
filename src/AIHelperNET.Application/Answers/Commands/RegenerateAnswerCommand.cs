using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Command to manually regenerate an answer for a conversation turn.</summary>
/// <param name="SessionId">The session containing the turn.</param>
/// <param name="TurnId">The turn to regenerate an answer for.</param>
/// <param name="ScreenContext">Optional OCR context to include.</param>
public sealed record RegenerateAnswerCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string? ScreenContext = null) : IRequest<Result>;

/// <summary>Handles <see cref="RegenerateAnswerCommand"/>.</summary>
public sealed class RegenerateAnswerHandler(
    IMediator mediator) : IRequestHandler<RegenerateAnswerCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(RegenerateAnswerCommand request, CancellationToken cancellationToken)
        => await mediator.Send(new GenerateAnswerCommand(
            request.SessionId, request.TurnId,
            AnswerVersionType.ManuallyRegenerated,
            request.ScreenContext), cancellationToken);
}
