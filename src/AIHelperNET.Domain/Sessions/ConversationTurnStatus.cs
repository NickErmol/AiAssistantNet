namespace AIHelperNET.Domain.Sessions;

/// <summary>Lifecycle state of a conversation turn.</summary>
public enum ConversationTurnStatus
{
    /// <summary>Question detected, answer not yet started.</summary>
    Detected,
    /// <summary>Generating the preliminary answer.</summary>
    GeneratingPreliminary,
    /// <summary>Preliminary answer is ready to display.</summary>
    PreliminaryReady,
    /// <summary>Waiting for a clarification response from the candidate.</summary>
    AwaitingClarification,
    /// <summary>Clarification received; ready to generate refined answer.</summary>
    ClarificationReceived,
    /// <summary>Generating the refined answer after clarification.</summary>
    GeneratingRefined,
    /// <summary>Refined answer is ready to display.</summary>
    RefinedReady,
    /// <summary>Turn dismissed by the user.</summary>
    Dismissed,
    /// <summary>Turn resolved — answer was used.</summary>
    Resolved,

    /// <summary>Accumulating multi-fragment question, no answer yet.</summary>
    CollectingQuestion = 9,

    /// <summary>Got continuation/requirement after answer, ready to refine.</summary>
    UpdatedContextReceived = 10
}
