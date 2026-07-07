using System.IO;
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class ResilientTranscriptionServiceTests
{
    private static TranscriptionOptions Options()
        => new(WhisperModelSize.LargeTurbo, "auto", new HashSet<string>());

    private static async IAsyncEnumerable<AudioFrame> Frames(
        int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            // Samples[0] carries the frame index so tests can assert continuity.
            yield return new AudioFrame([i], Speaker.Other, DateTimeOffset.UtcNow);
            await Task.Yield();
        }
    }

    private static async Task<List<TranscriptSegment>> Collect(IAsyncEnumerable<TranscriptSegment> s)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var x in s) list.Add(x);
        return list;
    }

    [Fact]
    public async Task PrimaryHealthy_PassesSegmentsThrough_NeverTouchesFallback()
    {
        var primary = new ScriptableStt((frames, ct) => Healthy(frames, ct));
        var fallback = new FrameRecordingStt();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier: null);

        var segments = await Collect(sut.TranscribeAsync(Frames(3), Options(), CancellationToken.None));

        segments.Should().ContainSingle(s => s.Text == "all good here friend");
        fallback.Consumed.Should().BeEmpty();

        static async IAsyncEnumerable<TranscriptSegment> Healthy(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var _ in frames.WithCancellation(ct)) { }
            yield return new TranscriptSegment("all good here friend", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
        }
    }

    [Fact]
    public async Task PrimaryFaultsTwice_FallsBackToFallback_WithRemainingFrames_AndNotifies()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => FaultAfterConsuming(frames, ct));
        var fallback = new FrameRecordingStt();
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier);

        await Collect(sut.TranscribeAsync(Frames(10), Options(), CancellationToken.None));

        attempts.Should().Be(2);   // initial + one reconnect
        // Attempt 1 consumed frames 0-3, attempt 2 consumed nothing before faulting;
        // fallback gets everything left in the buffer — no frame lost after the fault point.
        fallback.Consumed.Select(f => (int)f.Samples[0]).Should().BeEquivalentTo([4, 5, 6, 7, 8, 9]);
        notifier.Received(1).Notify(Arg.Is<string>(m => m.Contains("Whisper")));

        async IAsyncEnumerable<TranscriptSegment> FaultAfterConsuming(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1)
            {
                var n = 0;
                await foreach (var _ in frames.WithCancellation(ct))
                    if (++n == 4) break;   // consumed 4 frames, then the socket "dies"
            }
            throw new IOException("socket dropped");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task PrimaryFaultsOnce_ReconnectSucceeds_NoFallback()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => FlakyThenHealthy(frames, ct));
        var fallback = new FrameRecordingStt();
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier);

        var segments = await Collect(sut.TranscribeAsync(Frames(5), Options(), CancellationToken.None));

        attempts.Should().Be(2);
        segments.Should().ContainSingle(s => s.Text == "recovered on second attempt");
        fallback.Consumed.Should().BeEmpty();
        notifier.DidNotReceiveWithAnyArgs().Notify(default!);

        async IAsyncEnumerable<TranscriptSegment> FlakyThenHealthy(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1) throw new IOException("connect refused");
            await foreach (var _ in frames.WithCancellation(ct)) { }
            yield return new TranscriptSegment("recovered on second attempt", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
        }
    }

    [Fact]
    public async Task SegmentsEmittedBeforeAFault_AreNotLost()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => EmitThenFault(frames, ct));
        var fallback = new FrameRecordingStt();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier: null);

        var segments = await Collect(sut.TranscribeAsync(Frames(4), Options(), CancellationToken.None));

        segments.Should().Contain(s => s.Text == "first utterance made it out");

        async IAsyncEnumerable<TranscriptSegment> EmitThenFault(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1)
            {
                yield return new TranscriptSegment("first utterance made it out", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
                throw new IOException("mid-stream drop");
            }
            await foreach (var _ in frames.WithCancellation(ct)) { }
        }
    }
}
