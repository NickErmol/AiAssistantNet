using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Runs the primary (Deepgram) stream with Whisper as understudy. Frames are pumped through an
/// internal channel; on a primary fault it retries once, then falls back to Whisper for the rest
/// of the session. Unconsumed frames stay queued across the switch, so no audio after the fault
/// point is lost. (The Whisper model is already pre-warmed at app startup regardless of provider.)
/// </summary>
public sealed class ResilientTranscriptionService(
    ITranscriptionService primary,
    ITranscriptionService fallback,
    IOverlayStatusNotifier? notifier) : ITranscriptionService
{
    private const int MaxPrimaryAttempts = 2;   // initial + one reconnect

    /// <inheritdoc />
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in frames.WithCancellation(ct))
                    await buffer.Writer.WriteAsync(frame, ct);
            }
            catch (OperationCanceledException) { }
            finally { buffer.Writer.TryComplete(); }
        }, CancellationToken.None);

        for (var attempt = 1; attempt <= MaxPrimaryAttempts; attempt++)
        {
            var faulted = false;
            // Channel readers survive re-enumeration: whatever this attempt doesn't consume
            // stays queued for the next attempt (or the fallback).
            var stream = primary.TranscribeAsync(buffer.Reader.ReadAllAsync(ct), options, ct);
            await using var e = stream.GetAsyncEnumerator(ct);
            while (true)
            {
                TranscriptSegment segment;
                try
                {
                    if (!await e.MoveNextAsync()) break;   // completed normally
                    segment = e.Current;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Primary STT stream faulted (attempt {Attempt}/{Max})",
                        attempt, MaxPrimaryAttempts);
                    faulted = true;
                    break;
                }
                yield return segment;
            }

            if (!faulted)
            {
                await pump;
                yield break;
            }
        }

        Log.Warning("Primary STT unavailable after retry — using local Whisper for the rest of this session");
        notifier?.Notify("STT: using local Whisper (Deepgram unavailable)");

        await foreach (var segment in fallback.TranscribeAsync(buffer.Reader.ReadAllAsync(ct), options, ct))
            yield return segment;
        await pump;
    }
}
