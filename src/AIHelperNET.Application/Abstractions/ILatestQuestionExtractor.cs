using AIHelperNET.Application.Answers;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Port that derives the most-recent question-in-discussion (and its context) from a window of
/// recent transcript lines plus optional on-screen capture text. Used by the manual
/// Answer-latest-question hotkey to recover a question the live pipeline missed.
/// </summary>
public interface ILatestQuestionExtractor
{
    /// <summary>Derives the latest question from the given context.</summary>
    /// <param name="window">Recent transcript lines, oldest → newest (untrusted data).</param>
    /// <param name="screenContext">Combined recent-capture OCR, or null if none (untrusted data).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The derived question, or <see cref="LatestQuestionResult.None"/> if none was found.</returns>
    Task<LatestQuestionResult> ExtractAsync(
        IReadOnlyList<TranscriptLine> window, string? screenContext, CancellationToken ct);
}
