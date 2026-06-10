using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Answers;

/// <summary>Immutable snapshot of the captured screen task currently in focus.</summary>
/// <param name="ScreenCardId">The original capture card (lineage anchor).</param>
/// <param name="TopicLabel">One-line task label (card title + classifier context).</param>
/// <param name="Ocr">Combined OCR of the captured task.</param>
/// <param name="Mode">The screen analysis mode the capture used.</param>
/// <param name="Additions">Accumulated interviewer additions (oldest → newest).</param>
/// <param name="LatestCardId">Most recent card in the lineage — the parent for the next follow-up.</param>
public sealed record ScreenTaskContext(
    ConversationTurnId ScreenCardId,
    string TopicLabel,
    string Ocr,
    ScreenAnalysisMode Mode,
    IReadOnlyList<string> Additions,
    ConversationTurnId LatestCardId);

/// <summary>
/// Thread-safe, in-memory record of the captured screen task in focus. Written by the VM's capture
/// path (<see cref="Register"/>) and the follow-up handler (<see cref="SetLatestCard"/>); read and
/// accumulated by the transcript pipeline. Per-session — cleared on session start.
/// </summary>
public sealed class ScreenTaskContextStore
{
    private const int MaxAdditions = 8;
    private readonly object _gate = new();
    private ScreenTaskContext? _current;

    /// <summary>The current screen task snapshot, or <see langword="null"/> if none is in focus.</summary>
    public ScreenTaskContext? Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>Registers (new task) or refreshes (same card, updated OCR) the screen task in focus.</summary>
    /// <param name="cardId">The capture card id.</param>
    /// <param name="ocr">Combined OCR of the captured task.</param>
    /// <param name="mode">The screen analysis mode used.</param>
    /// <param name="isNewGroup">True when this capture starts a fresh task (resets additions).</param>
    public void Register(ConversationTurnId cardId, string ocr, ScreenAnalysisMode mode, bool isNewGroup)
    {
        lock (_gate)
        {
            if (!isNewGroup && _current is not null && _current.ScreenCardId == cardId)
            {
                _current = _current with { Ocr = ocr, Mode = mode };
                return;
            }
            _current = new ScreenTaskContext(cardId, ScreenTopic.Derive(ocr), ocr, mode, [], cardId);
        }
    }

    /// <summary>Appends an interviewer addition, keeping at most the most recent eight.</summary>
    /// <param name="text">The interviewer utterance to accumulate.</param>
    public void AddAddition(string text)
    {
        lock (_gate)
        {
            if (_current is null) return;
            var list = _current.Additions.Append(text).ToList();
            if (list.Count > MaxAdditions) list = list.GetRange(list.Count - MaxAdditions, MaxAdditions);
            _current = _current with { Additions = list };
        }
    }

    /// <summary>Points the lineage at the newest follow-up card (the next follow-up's parent).</summary>
    /// <param name="id">The new card id.</param>
    public void SetLatestCard(ConversationTurnId id)
    {
        lock (_gate)
        {
            if (_current is not null) _current = _current with { LatestCardId = id };
        }
    }

    /// <summary>Drops the screen task in focus (interviewer moved on, or session reset).</summary>
    public void Clear()
    {
        lock (_gate) { _current = null; }
    }
}
