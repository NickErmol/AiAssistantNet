namespace AIHelperNET.Domain.Sessions;

/// <summary>Indicates what triggered the creation of an answer version.</summary>
public enum AnswerVersionType
{
    /// <summary>First answer generated without additional context.</summary>
    Preliminary,
    /// <summary>Regenerated after the candidate provided a clarification.</summary>
    RefinedAfterClarification,
    /// <summary>Regenerated with screen OCR context included.</summary>
    UpdatedWithScreen,
    /// <summary>Manually triggered regeneration.</summary>
    ManuallyRegenerated,
    /// <summary>Continuation of a previous answer with user-supplied follow-up text.</summary>
    FollowUp
}
