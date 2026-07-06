using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Answers;

/// <summary>
/// Decides when a captured screen task has gone stale and its focus should be released. MOVED_ON is
/// not a reliable exit: when the captured task text is garbage (e.g. OCR'd meeting chrome), the
/// follow-up classifier answers Noise forever and the focus never releases — in a live session this
/// held for 19 minutes. Focus is released after too many consecutive Noise verdicts, or when no
/// FollowUp has arrived within the idle window. The idle default is deliberately generous (10 min):
/// silent multi-minute working stretches are normal while a candidate codes, and the consecutive-
/// Noise counter is the fast exit for garbage tasks. Pure — the caller supplies the clock.
/// </summary>
public sealed class ScreenFocusReleaseValve(int maxConsecutiveNoise = 5, TimeSpan? maxIdle = null)
{
    private readonly int _maxConsecutiveNoise = maxConsecutiveNoise >= 1
        ? maxConsecutiveNoise
        : throw new ArgumentOutOfRangeException(nameof(maxConsecutiveNoise), "Must be at least 1.");
    private readonly TimeSpan _maxIdle = maxIdle ?? TimeSpan.FromMinutes(10);
    private ConversationTurnId? _cardId;
    private int _consecutiveNoise;
    private DateTimeOffset _anchor;

    /// <summary>Tracks one follow-up outcome for the screen card in focus and returns
    /// <see langword="true"/> when the focus should be released.</summary>
    /// <param name="cardId">The screen card currently in focus (a new id resets tracking).</param>
    /// <param name="outcome">The classifier's verdict for the latest interviewer utterance.</param>
    /// <param name="now">The current time.</param>
    public bool Track(ConversationTurnId cardId, ScreenFollowUpOutcome outcome, DateTimeOffset now)
    {
        if (_cardId != cardId)
        {
            _cardId = cardId;
            _consecutiveNoise = 0;
            _anchor = now;
        }

        // A FollowUp (or MovedOn, which clears focus anyway) proves the task is alive.
        if (outcome != ScreenFollowUpOutcome.Noise)
        {
            _consecutiveNoise = 0;
            _anchor = now;
            return false;
        }

        _consecutiveNoise++;
        var release = _consecutiveNoise >= _maxConsecutiveNoise || now - _anchor >= _maxIdle;
        if (release)
            Reset();
        return release;
    }

    /// <summary>Clears all tracking state (session reset).</summary>
    public void Reset()
    {
        _cardId = null;
        _consecutiveNoise = 0;
        _anchor = default;
    }
}
