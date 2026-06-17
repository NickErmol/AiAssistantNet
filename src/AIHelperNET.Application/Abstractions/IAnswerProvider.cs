using AIHelperNET.Application.Answers;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for streaming AI-generated answers.</summary>
public interface IAnswerProvider
{
    /// <summary>Gets the backend this provider targets.</summary>
    AiBackend Backend { get; }

    /// <summary>Streams answer tokens for the given prompt.</summary>
    /// <param name="prompt">The structured prompt to send.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerPrompt prompt, CancellationToken ct);

    /// <summary>
    /// Best-effort priming of the network path (DNS/TLS/connection pool) so the first real answer
    /// call doesn't pay the handshake. Default is a no-op; providers may override. Must never throw.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task WarmUpAsync(CancellationToken ct) => Task.CompletedTask;
}
