namespace AIHelperNET.Application.Answers;

/// <summary>Determines the analysis strategy applied when processing on-screen content.</summary>
public enum ScreenAnalysisMode
{
    /// <summary>General-purpose analysis with no specific focus.</summary>
    General,

    /// <summary>Solve a coding task visible on screen.</summary>
    SolveCodingTask,

    /// <summary>Debug an error or stack trace visible on screen.</summary>
    DebugError,

    /// <summary>Explain the code visible on screen.</summary>
    ExplainCode,

    /// <summary>Provide a high-level system design for requirements on screen.</summary>
    SystemDesign
}
