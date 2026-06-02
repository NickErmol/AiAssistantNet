using System.Security;
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Command to persist an AI API key in the secret store.</summary>
/// <param name="Key">The API key as a secure string.</param>
public sealed record SaveApiKeyCommand(SecureString Key) : IRequest<Result>;

/// <summary>Handles <see cref="SaveApiKeyCommand"/>.</summary>
public sealed class SaveApiKeyHandler(ISecretStore secretStore)
    : IRequestHandler<SaveApiKeyCommand, Result>
{
    /// <inheritdoc/>
    public ValueTask<Result> Handle(SaveApiKeyCommand command, CancellationToken cancellationToken)
        => ValueTask.FromResult(secretStore.SaveApiKey(command.Key));
}
