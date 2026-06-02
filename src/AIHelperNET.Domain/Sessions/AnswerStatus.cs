namespace AIHelperNET.Domain.Sessions;

/// <summary>
/// Represents the status of an answer being generated or displayed.
/// </summary>
public enum AnswerStatus
{
    /// <summary>The answer is currently being streamed/generated.</summary>
    Streaming,

    /// <summary>The answer generation has completed.</summary>
    Completed,

    /// <summary>The answer generation was cancelled.</summary>
    Cancelled,

    /// <summary>The answer generation failed.</summary>
    Failed
}
