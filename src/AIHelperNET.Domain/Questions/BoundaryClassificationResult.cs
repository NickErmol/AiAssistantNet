namespace AIHelperNET.Domain.Questions;

/// <summary>
/// Result of classifying a speech segment boundary with decision flags and confidence.
/// </summary>
/// <param name="Classification">The boundary label assigned to the segment.</param>
/// <param name="Confidence">Confidence score from 0.0 (low) to 1.0 (high).</param>
/// <param name="ShouldGenerateAnswer">Whether an answer should be generated for this segment.</param>
/// <param name="ShouldRefineExistingAnswer">Whether an existing answer should be refined based on this segment.</param>
/// <param name="ShouldCreateNewTurn">Whether a new conversation turn should be created.</param>
/// <param name="NormalizedQuestionText">The normalized text of the question (if applicable).</param>
/// <param name="Reason">Human-readable explanation of the classification decision.</param>
public sealed record BoundaryClassificationResult(
    BoundaryLabel Classification,
    double Confidence,
    bool ShouldGenerateAnswer,
    bool ShouldRefineExistingAnswer,
    bool ShouldCreateNewTurn,
    string NormalizedQuestionText,
    string Reason)
{
    /// <summary>
    /// Creates an ambiguous classification result indicating the segment requires AI classification.
    /// </summary>
    /// <param name="text">The text of the ambiguous segment.</param>
    /// <returns>A <see cref="BoundaryClassificationResult"/> with <see cref="BoundaryLabel.NoQuestion"/> and 0.30 confidence.</returns>
    public static BoundaryClassificationResult Ambiguous(string text) =>
        new(
            Classification: BoundaryLabel.NoQuestion,
            Confidence: 0.30,
            ShouldGenerateAnswer: false,
            ShouldRefineExistingAnswer: false,
            ShouldCreateNewTurn: false,
            NormalizedQuestionText: text,
            Reason: "Classification ambiguous; requires AI classifier");
}
