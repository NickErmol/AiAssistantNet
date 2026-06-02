using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for pushing streamed answer chunks to the UI layer.</summary>
public interface IAnswerStreamSink
{
    /// <summary>Pushes a single streamed chunk to the sink.</summary>
    /// <param name="answerId">The answer being streamed.</param>
    /// <param name="chunk">The text chunk.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask PushAsync(AnswerId answerId, string chunk, CancellationToken ct);
}
