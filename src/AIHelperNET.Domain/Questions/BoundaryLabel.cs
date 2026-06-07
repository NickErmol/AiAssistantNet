namespace AIHelperNET.Domain.Questions;

/// <summary>
/// Classification label for question boundaries used in speech segment analysis.
/// </summary>
public enum BoundaryLabel
{
    /// <summary>Text is not a question or task.</summary>
    NoQuestion,

    /// <summary>Scenario setup, not yet answerable.</summary>
    QuestionStarted,

    /// <summary>Extends an in-progress collecting-question turn.</summary>
    QuestionContinued,

    /// <summary>Explicit question (ends with "?" or interrogative + context).</summary>
    QuestionComplete,

    /// <summary>Imperative task (Explain/Design/etc).</summary>
    TaskComplete,

    /// <summary>Speaker adds clarification context.</summary>
    ClarificationOfCurrentQuestion,

    /// <summary>Adds new constraint to already-answered question.</summary>
    AdditionalRequirement,

    /// <summary>New topic with explicit question/task marker.</summary>
    NewQuestion,

    /// <summary>Small talk, filler, unrelated speech.</summary>
    Unrelated
}
