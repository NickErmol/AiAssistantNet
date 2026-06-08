namespace AIHelperNET.Application.Sessions;

/// <summary>The action to take for a candidate <c>NewQuestion</c> split.</summary>
public enum SplitDecision
{
    /// <summary>Dismiss-and-open: treat the segment as a genuinely new question.</summary>
    Split,

    /// <summary>Suppress the split: append the segment to the live turn instead.</summary>
    AppendToActiveTurn
}

/// <summary>
/// Pure guard protecting the destructive <c>NewQuestion</c> split (which dismisses a live turn and
/// opens a new card). Composes the recency, asymmetric-confidence, and (via the supplied effective
/// confidence) heuristic/AI-agreement guards. No I/O, no clock — deterministically unit-testable.
/// </summary>
public sealed class BoundarySplitGuard
{
    /// <summary>Seconds since the live turn's last activity, within which a split needs high confidence.</summary>
    public const double RecencyWindowSeconds = 6.0;

    /// <summary>Effective-confidence required to split a recent, live turn.</summary>
    public const double SplitConfidenceBar = 0.90;

    /// <summary>
    /// Decides whether a <c>NewQuestion</c> should split off a new turn or append to the live one.
    /// </summary>
    /// <param name="effectiveConfidence">Confidence after any agreement demotion (see SplitConfidence, added in Task 2).</param>
    /// <param name="hasLiveTurn">Whether a non-terminal active turn exists to protect.</param>
    /// <param name="sinceLastActivity">Elapsed time since that turn was last active.</param>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance semantics preserved for future state in subsequent tasks.")]
    public SplitDecision Evaluate(double effectiveConfidence, bool hasLiveTurn, TimeSpan sinceLastActivity)
    {
        if (!hasLiveTurn)
            return SplitDecision.Split;

        if (sinceLastActivity > TimeSpan.FromSeconds(RecencyWindowSeconds))
            return SplitDecision.Split;

        return effectiveConfidence >= SplitConfidenceBar
            ? SplitDecision.Split
            : SplitDecision.AppendToActiveTurn;
    }
}
