using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Deterministic <see cref="IQuestionBoundaryClassifier"/> that dequeues scripted results in FIFO
/// order. When the queue is empty it returns <see cref="BoundaryClassificationResult.Ambiguous"/>.
/// </summary>
public sealed class FakeQuestionBoundaryClassifier : IQuestionBoundaryClassifier
{
    private readonly ConcurrentQueue<BoundaryClassificationResult> _scripted = new();

    /// <summary>Enqueues the next scripted result to be returned by <see cref="ClassifyAsync"/>.</summary>
    public void Enqueue(BoundaryClassificationResult result) => _scripted.Enqueue(result);

    /// <inheritdoc/>
    public Task<BoundaryClassificationResult> ClassifyAsync(
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<TranscriptItem> recentItems,
        TranscriptItem latestItem,
        Speaker speaker,
        CancellationToken ct)
        => Task.FromResult(_scripted.TryDequeue(out var r)
            ? r
            : BoundaryClassificationResult.Ambiguous(latestItem.Text));
}
