using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Deterministic <see cref="IAnswerProvider"/> that returns a canned answer which embeds the user
/// prompt. Echoing the prompt lets a test assert that specific folded context (e.g. a clarification)
/// reached the model.
/// </summary>
public sealed class FakeAnswerProvider : IAnswerProvider
{
    /// <inheritdoc/>
    public AiBackend Backend => AiBackend.Claude;

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "ANSWER>> ";
        await Task.Yield();
        yield return prompt.User;
    }
}

/// <summary>Resolver that always returns the supplied <see cref="FakeAnswerProvider"/>.</summary>
public sealed class FakeAnswerProviderResolver(IAnswerProvider provider) : IAnswerProviderResolver
{
    /// <inheritdoc/>
    public IAnswerProvider Resolve(AiBackend backend) => provider;
}
