using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Pure guard that suppresses a <em>fold-into-an-existing-turn</em> when the latest transcript
/// item's ASR (speech-recognition) confidence is too low to trust. Garbled audio that Whisper
/// transcribes into plausible-but-wrong words can be confidently mis-labelled as a continuation,
/// silently corrupting a good turn (see the 2026-06-11 field example). Whisper is often
/// confidently wrong on the <em>words</em>, so the only independent signal is the segment
/// probability — which the text-only boundary classifier never sees. This gate reads it.
/// No I/O, no clock — deterministically unit-testable.
/// </summary>
public sealed class AsrConfidenceGate
{
    /// <summary>
    /// Segment-confidence floor below which a fold is suppressed. Conservative starting value;
    /// tune from the <c>AsrConfidence</c> field now recorded on every boundary decision
    /// (<c>boundary-decisions-*.jsonl</c>).
    /// </summary>
    public const double AsrFloor = 0.45;

    /// <summary>
    /// Decides whether a fold-into-the-live-turn should be dropped as untrusted noise.
    /// </summary>
    /// <param name="asrConfidence">The latest item's Whisper segment probability (0..1).</param>
    /// <param name="foldLabel">The label that would drive routing. For a <c>NewQuestion</c> the
    /// split guard demoted to an append, pass <see cref="BoundaryLabel.QuestionContinued"/>.</param>
    /// <param name="liveTurnExists">Whether a non-terminal active turn exists to protect.</param>
    /// <returns><see langword="true"/> to drop (suppress the fold); otherwise route unchanged.</returns>
#pragma warning disable CA1822 // instance method intentional — callers hold an AsrConfidenceGate reference
    public bool ShouldDrop(double asrConfidence, BoundaryLabel foldLabel, bool liveTurnExists)
#pragma warning restore CA1822
    {
        if (!liveTurnExists)
            return false;
        if (asrConfidence >= AsrFloor)
            return false;
        return IsFoldLabel(foldLabel);
    }

    /// <summary>Fold labels: routes that append the item into the live turn (corruption surface).</summary>
    /// <param name="label">The label to test.</param>
    /// <returns><see langword="true"/> if the label folds into an existing turn.</returns>
    public static bool IsFoldLabel(BoundaryLabel label) =>
        label is BoundaryLabel.QuestionContinued
              or BoundaryLabel.AdditionalRequirement
              or BoundaryLabel.ClarificationOfCurrentQuestion;
}
