namespace AIHelperNET.Domain.Questions;

/// <summary>The result of evaluating a transcript segment for question detection.</summary>
/// <param name="IsQuestion">Whether the text looks like a question.</param>
/// <param name="IsDuplicate">Whether the question is a near-duplicate of a recent question.</param>
/// <param name="NormalizedText">The trimmed text when a new question is detected; otherwise <see langword="null"/>.</param>
public sealed record QuestionDetectionResult(bool IsQuestion, bool IsDuplicate, string? NormalizedText)
{
    /// <summary>Returns a result indicating the text is not a question.</summary>
    public static QuestionDetectionResult NotAQuestion() => new(false, false, null);

    /// <summary>Returns a result indicating the text is a near-duplicate of a recent question.</summary>
    public static QuestionDetectionResult Duplicate() => new(true, true, null);

    /// <summary>Returns a result for a newly detected question with the given normalised text.</summary>
    /// <param name="t">The normalised question text.</param>
    public static QuestionDetectionResult NewQuestion(string t) => new(true, false, t);
}
