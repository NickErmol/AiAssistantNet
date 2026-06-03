using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to remove the stored AI API key from the secret store.</summary>
public sealed record DeleteApiKeyCommand : IRequest<Result>;

/// <summary>Handles <see cref="DeleteApiKeyCommand"/>.</summary>
public sealed class DeleteApiKeyHandler(ISecretStore secretStore)
    : IRequestHandler<DeleteApiKeyCommand, Result>
{
    /// <inheritdoc/>
    public ValueTask<Result> Handle(DeleteApiKeyCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(secretStore.DeleteApiKey());
}
