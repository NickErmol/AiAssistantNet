namespace AIHelperNET.Application.Answers;

/// <summary>A recent screen capture passed to the Answer-latest-question flow.</summary>
/// <param name="AgeLabel">Human-readable age, e.g. "35s ago".</param>
/// <param name="Ocr">The capture's OCR text (untrusted data).</param>
public sealed record RecentCapture(string AgeLabel, string Ocr);

/// <summary>One transcript line in the look-back window, with a role label.</summary>
/// <param name="Speaker">Role label: "Interviewer" or "Candidate".</param>
/// <param name="Text">The transcribed text (untrusted data).</param>
public sealed record TranscriptLine(string Speaker, string Text);

/// <summary>Outcome of deriving the latest question from recent context.</summary>
/// <param name="Found">True if a question was identified.</param>
/// <param name="QuestionText">The derived question (empty when not found).</param>
/// <param name="ContextSummary">A short context summary for the question (may be empty).</param>
public sealed record LatestQuestionResult(bool Found, string QuestionText, string ContextSummary)
{
    /// <summary>A "no question found" result.</summary>
    public static LatestQuestionResult None { get; } = new(false, string.Empty, string.Empty);
}
