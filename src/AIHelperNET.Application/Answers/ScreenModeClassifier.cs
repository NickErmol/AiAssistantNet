using System;

namespace AIHelperNET.Application.Answers;

/// <summary>Maps interviewer speech to a <see cref="ScreenAnalysisMode"/> using deterministic
/// keyword matching, so a spoken instruction ("write a SQL", "fix the bug") pre-selects the
/// screen-analysis mode before the candidate captures. Pure — no I/O, no AI call.</summary>
public static class ScreenModeClassifier
{
    // Ordered most-specific first: "fix the code" / "explain the code" must win over the generic
    // coding bucket, so DebugError and ExplainCode are checked before SolveCodingTask.
    private static readonly (ScreenAnalysisMode Mode, string[] Phrases)[] Rules =
    [
        (ScreenAnalysisMode.DebugError, new[]
        {
            "fix the bug", "fix this bug", "fix the error", "fix this error", "fix the code",
            "fix this code", "debug this", "debug the", "what's wrong with", "what is wrong with",
            "why is this failing", "why does this fail", "why is it failing",
        }),
        (ScreenAnalysisMode.ExplainCode, new[]
        {
            "explain this code", "explain the code", "what does this code do", "what does this do",
            "walk me through this code", "walk me through the code",
        }),
        (ScreenAnalysisMode.SystemDesign, new[]
        {
            "design a system", "system design", "how would you design", "design the architecture",
            "architect this", "architect the",
        }),
        (ScreenAnalysisMode.SolveCodingTask, new[]
        {
            "write a sql", "write sql", "write a query", "write the query", "write a code",
            "write code", "write a function", "write a method", "write a script", "implement",
            "solve this task", "solve the task", "code this",
        }),
    ];

    /// <summary>Returns the matching mode on a confident keyword hit, or <see langword="null"/>
    /// when the text carries no clear screen-task instruction.</summary>
    /// <param name="interviewerText">The most recent interviewer transcript line.</param>
    public static ScreenAnalysisMode? Classify(string interviewerText)
    {
        if (string.IsNullOrWhiteSpace(interviewerText))
            return null;

        foreach (var (mode, phrases) in Rules)
            foreach (var phrase in phrases)
                if (interviewerText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    return mode;

        return null;
    }
}
