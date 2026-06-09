using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Test <see cref="ITranscriptSink"/> that records every transcript item (speaker + text) so
/// scenarios can assert the per-speaker transcript produced by the real Whisper path.
/// </summary>
public sealed class CapturingTranscriptSink : ITranscriptSink
{
    private readonly ConcurrentQueue<TranscriptItem> _items = new();

    /// <summary>All transcript items pushed, in arrival order.</summary>
    public IReadOnlyList<TranscriptItem> Items => [.. _items];

    /// <summary>Transcript lines for one speaker, in order.</summary>
    public IReadOnlyList<string> TextFor(Speaker speaker) =>
        [.. _items.Where(i => i.Speaker == speaker).Select(i => i.Text)];

    /// <inheritdoc/>
    public void OnTranscriptItem(TranscriptItem item) => _items.Enqueue(item);
}
