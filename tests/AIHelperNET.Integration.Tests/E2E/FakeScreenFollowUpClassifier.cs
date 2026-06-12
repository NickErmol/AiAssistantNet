using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Deterministic <see cref="IScreenFollowUpClassifier"/> that dequeues scripted outcomes in FIFO
/// order. When the queue is empty it returns <see cref="ScreenFollowUpOutcome.Noise"/> (ignore),
/// so an unscripted utterance can never spawn a card by accident.
/// </summary>
public sealed class FakeScreenFollowUpClassifier : IScreenFollowUpClassifier
{
    private readonly ConcurrentQueue<ScreenFollowUpOutcome> _scripted = new();

    /// <summary>Enqueues the next scripted outcome to be returned by <see cref="ClassifyAsync"/>.</summary>
    public void Enqueue(ScreenFollowUpOutcome outcome) => _scripted.Enqueue(outcome);

    /// <inheritdoc/>
    public Task<ScreenFollowUpOutcome> ClassifyAsync(
        string taskSummary, IReadOnlyList<string> additions, string utterance, CancellationToken ct)
        => Task.FromResult(_scripted.TryDequeue(out var o) ? o : ScreenFollowUpOutcome.Noise);
}
