using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>One scripted spoken utterance. <paramref name="GapMsBefore"/> is the real delay emitted
/// before this utterance's frame, used to drive the segment-merge timing in SessionRunner.</summary>
public sealed record ScriptedUtterance(Speaker Speaker, string Text, int GapMsBefore);

/// <summary>Test <see cref="IAudioCaptureService"/> that replays a scripted utterance list as exactly
/// one <see cref="AudioFrame"/> each (utterance index encoded in <c>Samples[0]</c> for correlation),
/// then completes the stream so the SessionRunner loop terminates on its own.</summary>
public sealed class ScriptedAudioCaptureService(IReadOnlyList<ScriptedUtterance> script) : IAudioCaptureService
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection, [EnumeratorCancellation] CancellationToken ct)
    {
        var clock = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < script.Count; i++)
        {
            if (script[i].GapMsBefore > 0)
                await Task.Delay(script[i].GapMsBefore, ct);
            clock = clock.AddSeconds(1);
            yield return new AudioFrame([(float)i], script[i].Speaker, clock);
        }
    }
}

/// <summary>Test <see cref="ITranscriptionService"/> that maps each <see cref="AudioFrame"/> to the
/// scripted <see cref="TranscriptSegment"/> identified by the index in <c>Samples[0]</c>. The script
/// is read-only, so the concurrent mic/loopback invocations need no synchronization.</summary>
public sealed class ScriptedTranscriptionService(IReadOnlyList<ScriptedUtterance> script) : ITranscriptionService
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize model, string language,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in frames.WithCancellation(ct))
        {
            var utt = script[(int)frame.Samples[0]];
            yield return new TranscriptSegment(utt.Text, frame.Speaker, frame.CapturedAt, 0.95f);
        }
    }
}
