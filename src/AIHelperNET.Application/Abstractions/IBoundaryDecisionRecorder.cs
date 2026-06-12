using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// A single boundary-routing decision: both classifier opinions, the agreement outcome, the guard
/// result, and the final route. Durable so it can be inspected and replayed (Spec 3b corpus).
/// Records are written for interviewer (<see cref="Speaker.Other"/>) utterances only — candidate
/// (<see cref="Speaker.Me"/>) utterances are routed deterministically and never reach the classifier.
/// </summary>
public sealed record BoundaryDecisionRecord(
    DateTimeOffset Timestamp,
    Guid SessionId,
    Guid? TurnId,
    Speaker Speaker,
    ConversationTurnStatus? StaleTurnStatus,
    BoundaryLabel HeuristicLabel,
    double HeuristicConfidence,
    BoundaryLabel? AiLabel,
    double? AiConfidence,
    bool Agreed,
    double EffectiveConfidence,
    double AsrConfidence,
    string Route,
    BoundaryLabel FinalLabel,
    string TextClip);

/// <summary>Port that durably records each boundary-routing decision. Implementations must be best-effort.</summary>
public interface IBoundaryDecisionRecorder
{
    /// <summary>Records one decision. Implementations must never throw — recording is diagnostic only.</summary>
    /// <param name="record">The decision to record.</param>
    void Record(BoundaryDecisionRecord record);
}
