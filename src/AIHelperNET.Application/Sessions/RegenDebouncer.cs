using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Per-turn debounce for continuation-driven regenerations. Each <see cref="Touch"/> (re)arms a
/// short timer for the turn; when the quiet window elapses the regeneration callback runs once.
/// A burst of fragments collapses into a single regeneration. Built on <see cref="TimeProvider"/>
/// so it is deterministically testable with <c>FakeTimeProvider</c>.
/// </summary>
public sealed class RegenDebouncer(TimeProvider time) : IDisposable
{
    private const int DebounceMs = 1000;

    private readonly object _gate = new();
    private readonly Dictionary<ConversationTurnId, ITimer> _timers = [];

    /// <summary>(Re)arms the debounce for <paramref name="turnId"/>. On elapse, runs <paramref name="fireRegen"/> once.</summary>
    public void Touch(ConversationTurnId turnId, Action fireRegen)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(turnId, out var existing))
            {
                existing.Change(TimeSpan.FromMilliseconds(DebounceMs), Timeout.InfiniteTimeSpan);
                return;
            }

            var timer = time.CreateTimer(
                _ =>
                {
                    lock (_gate)
                    {
                        if (_timers.Remove(turnId, out var t)) t.Dispose();
                    }
                    fireRegen();
                },
                state: null,
                dueTime: TimeSpan.FromMilliseconds(DebounceMs),
                period: Timeout.InfiniteTimeSpan);

            _timers[turnId] = timer;
        }
    }

    /// <summary>Drops any pending debounce for a turn (e.g. it went terminal).</summary>
    public void Cancel(ConversationTurnId turnId)
    {
        lock (_gate)
        {
            if (_timers.Remove(turnId, out var t)) t.Dispose();
        }
    }

    /// <summary>Cancels all pending timers.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var t in _timers.Values) t.Dispose();
            _timers.Clear();
        }
    }
}
