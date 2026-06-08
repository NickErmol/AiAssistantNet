using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Computes the effective confidence for a split decision, demoting it when the fast heuristic and
/// the AI classifier disagree about whether a segment is a new question (the "agreement" guard).
/// Pure and side-effect free.
/// </summary>
public static class SplitConfidence
{
    /// <summary>Multiplier applied to confidence when the two opinions disagree on splitting.</summary>
    public const double DisagreementPenalty = 0.5;

    /// <summary>Continuation-family labels: non-split labels that contradict a <c>NewQuestion</c>.</summary>
    /// <param name="label">The label to test.</param>
    /// <returns><see langword="true"/> if the label is a continuation-family label.</returns>
    public static bool IsContinuationFamily(BoundaryLabel label) =>
        label is BoundaryLabel.QuestionContinued
              or BoundaryLabel.ClarificationOfCurrentQuestion
              or BoundaryLabel.AdditionalRequirement;

    /// <summary>
    /// Returns the effective confidence and whether the two opinions agree about splitting.
    /// </summary>
    /// <param name="finalLabel">The label that would drive routing.</param>
    /// <param name="finalConfidence">Its reported confidence.</param>
    /// <param name="otherLabel">The other opinion (heuristic vs AI), or <see langword="null"/> if only one exists.</param>
    /// <returns>The effective confidence after any demotion, and whether the opinions agree on splitting.</returns>
    public static (double Effective, bool Agreed) Resolve(
        BoundaryLabel finalLabel, double finalConfidence, BoundaryLabel? otherLabel)
    {
        if (otherLabel is null)
            return (finalConfidence, true);

        var disagreeOnSplit =
            (finalLabel == BoundaryLabel.NewQuestion && IsContinuationFamily(otherLabel.Value)) ||
            (otherLabel.Value == BoundaryLabel.NewQuestion && IsContinuationFamily(finalLabel));

        return disagreeOnSplit
            ? (finalConfidence * DisagreementPenalty, false)
            : (finalConfidence, true);
    }
}
