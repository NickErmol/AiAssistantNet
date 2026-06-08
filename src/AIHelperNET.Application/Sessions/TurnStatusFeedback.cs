using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Unbounded, thread-safe <see cref="ITurnStatusFeedback"/> backed by a
/// <see cref="Channel{T}"/>. Registered as a singleton so the answer handler (multi-writer)
/// and the pipeline (single reader) share one instance.
/// </summary>
public sealed class TurnStatusFeedback : ITurnStatusFeedback
{
    private readonly Channel<TurnStatusEvent> _channel =
        Channel.CreateUnbounded<TurnStatusEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <inheritdoc/>
    public void Publish(TurnStatusEvent statusEvent) => _channel.Writer.TryWrite(statusEvent);

    /// <inheritdoc/>
    public bool TryDrain(out TurnStatusEvent statusEvent) => _channel.Reader.TryRead(out statusEvent);
}
