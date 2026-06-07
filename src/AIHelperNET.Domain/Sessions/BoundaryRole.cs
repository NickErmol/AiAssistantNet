namespace AIHelperNET.Domain.Sessions;

/// <summary>
/// Role of a speech segment in the context of question boundaries and conversation flow.
/// </summary>
public enum BoundaryRole
{
    /// <summary>No specific boundary role assigned.</summary>
    None = 0,

    /// <summary>Marks the start of a new question or task.</summary>
    QuestionStart,

    /// <summary>Marks a middle segment within a multi-fragment question.</summary>
    QuestionMiddle,

    /// <summary>Marks the end of a complete question or task.</summary>
    QuestionEnd,

    /// <summary>Provides clarification for the current question.</summary>
    Clarification,

    /// <summary>Adds an additional requirement to an already-answered question.</summary>
    AdditionalRequirement,

    /// <summary>Marks the start of a new, unrelated question.</summary>
    NewQuestion,

    /// <summary>Contains unrelated speech, small talk, or filler.</summary>
    Unrelated
}
