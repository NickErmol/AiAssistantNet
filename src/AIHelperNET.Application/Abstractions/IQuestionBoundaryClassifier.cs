using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for classifying transcript boundary context using an AI model.</summary>
public interface IQuestionBoundaryClassifier
{
    /// <summary>
    /// Classifies the latest transcript item in context, returning a boundary classification.
    /// </summary>
    /// <param name="activeTurnStatus">The status of the active conversation turn, or <see langword="null"/> if none.</param>
    /// <param name="recentItems">The last N transcript items for context.</param>
    /// <param name="latestItem">The transcript item to classify.</param>
    /// <param name="speaker">The speaker who produced the latest item.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The boundary classification result.</returns>
    Task<BoundaryClassificationResult> ClassifyAsync(
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<TranscriptItem> recentItems,
        TranscriptItem latestItem,
        Speaker speaker,
        CancellationToken ct);
}
