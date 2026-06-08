using System.Collections.Concurrent;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Test <see cref="IAnswerStreamSink"/> that accumulates streamed answer text per
/// (turn, version) and lets a driver deterministically await the Nth completion of a key.
/// A regenerated turn completes the same key more than once, so completions are counted.
/// </summary>
public sealed class CapturingAnswerStreamSink : IAnswerStreamSink
{
    private readonly record struct Key(ConversationTurnId TurnId, AnswerVersionType Version);

    private sealed class Entry
    {
        public readonly StringBuilder Text = new();
        public int CompletedCount;
        public TaskCompletionSource Pulse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly ConcurrentDictionary<Key, Entry> _entries = new();
    private readonly ConcurrentBag<string> _errors = [];

    /// <summary>Error messages reported via <see cref="OnErrorAsync"/>, for assertions.</summary>
    public IReadOnlyCollection<string> Errors => _errors;

    private Entry GetEntry(ConversationTurnId turnId, AnswerVersionType version)
        => _entries.GetOrAdd(new Key(turnId, version), _ => new Entry());

    /// <inheritdoc/>
    public ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct)
    {
        var entry = GetEntry(turnId, versionType);
        lock (entry) entry.Text.Append(chunk);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct)
    {
        var entry = GetEntry(turnId, versionType);
        TaskCompletionSource toSignal;
        lock (entry)
        {
            entry.CompletedCount++;
            toSignal = entry.Pulse;
            entry.Pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.SetResult();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnErrorAsync(ConversationTurnId turnId, string errorMessage, CancellationToken ct)
    {
        _errors.Add(errorMessage);
        return ValueTask.CompletedTask;
    }

    /// <summary>The accumulated answer text for a (turn, version), or empty if none.</summary>
    public string Text(ConversationTurnId turnId, AnswerVersionType version)
    {
        var entry = GetEntry(turnId, version);
        lock (entry) return entry.Text.ToString();
    }

    /// <summary>
    /// Awaits until the (turn, version) key has completed at least <paramref name="target"/> times,
    /// throwing <see cref="TimeoutException"/> if that does not happen within <paramref name="timeout"/>.
    /// </summary>
    public async Task WaitForCompletionCountAsync(
        ConversationTurnId turnId, AnswerVersionType version, int target, TimeSpan timeout)
    {
        var entry = GetEntry(turnId, version);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task pulse;
            lock (entry)
            {
                if (entry.CompletedCount >= target) return;
                pulse = entry.Pulse.Task;
            }
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException(
                    $"Answer for turn {turnId} {version} did not reach completion #{target} in {timeout}.");
            var done = await Task.WhenAny(pulse, Task.Delay(remaining));
            if (done != pulse && DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Answer for turn {turnId} {version} did not reach completion #{target} in {timeout}.");
        }
    }
}
