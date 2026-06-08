using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// A turn-lifecycle status transition reported by the background answer worker.
/// </summary>
/// <param name="TurnId">The conversation turn whose status changed.</param>
/// <param name="Status">The new status the turn transitioned to.</param>
public readonly record struct TurnStatusEvent(ConversationTurnId TurnId, ConversationTurnStatus Status);

/// <summary>
/// One-way, in-memory feedback channel from <c>GenerateAnswerHandler</c> (which runs in a separate
/// DI scope) back to <c>TranscriptPipelineService</c>, so the pipeline's authoritative in-memory
/// <c>Session</c> can be kept in sync with answer-generation progress.
/// </summary>
public interface ITurnStatusFeedback
{
    /// <summary>Publishes a status transition. Safe to call from any thread.</summary>
    /// <param name="statusEvent">The transition to report.</param>
    void Publish(TurnStatusEvent statusEvent);

    /// <summary>
    /// Removes and returns the next pending event if one is available.
    /// </summary>
    /// <param name="statusEvent">The dequeued event when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if an event was drained; otherwise <see langword="false"/>.</returns>
    bool TryDrain(out TurnStatusEvent statusEvent);
}
