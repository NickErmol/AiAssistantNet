using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

/// <summary>Query that returns whether an API key is currently stored.</summary>
public sealed record HasApiKeyQuery : IRequest<Result<bool>>;

/// <summary>Handles <see cref="HasApiKeyQuery"/>.</summary>
public sealed class HasApiKeyHandler(ISecretStore secretStore)
    : IRequestHandler<HasApiKeyQuery, Result<bool>>
{
    /// <inheritdoc/>
    public ValueTask<Result<bool>> Handle(HasApiKeyQuery query, CancellationToken cancellationToken)
        => ValueTask.FromResult(Result.Ok(secretStore.HasApiKey()));
}
