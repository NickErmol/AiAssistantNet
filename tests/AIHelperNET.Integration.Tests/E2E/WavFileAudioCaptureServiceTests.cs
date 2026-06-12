using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public sealed class WavFileAudioCaptureServiceTests
{
    [Fact]
    public async Task CaptureAsync_ReplaysTwoUtterances_ProducesCorrectSpeakerFramesInOrder()
    {
        // Arrange
        var script = new List<WavUtterance>
        {
            new(Speaker.Other, "other_di.wav", 0),
            new(Speaker.Me, "me_clarify.wav", 0),
        };
        var sut = new WavFileAudioCaptureService(script);

        // Act
        var frames = new List<AudioFrame>();
        await foreach (var frame in sut.CaptureAsync(new AudioDeviceSelection("mic", "loopback"), CancellationToken.None))
            frames.Add(frame);

        // Assert — both speakers are represented
        frames.Any(f => f.Speaker == Speaker.Other).Should().BeTrue("other_di.wav should produce Other frames");
        frames.Any(f => f.Speaker == Speaker.Me).Should().BeTrue("me_clarify.wav should produce Me frames");

        // All Other frames come before all Me frames (matches script order)
        var lastOtherIndex = frames.Select((f, i) => (f, i)).Last(x => x.f.Speaker == Speaker.Other).i;
        var firstMeIndex = frames.Select((f, i) => (f, i)).First(x => x.f.Speaker == Speaker.Me).i;
        lastOtherIndex.Should().BeLessThan(firstMeIndex, "Other utterance is scripted before Me utterance");

        // other_di.wav is ~3.3s at 16kHz → > 16000 total Other samples
        var otherSampleCount = frames
            .Where(f => f.Speaker == Speaker.Other)
            .Sum(f => f.Samples.Length);
        otherSampleCount.Should().BeGreaterThan(16_000, "other_di.wav is ~3.3s so should yield > 16000 samples");

        // Every frame's Samples length is between 1 and 512 (chunk size) inclusive
        frames.Should().AllSatisfy(f =>
            f.Samples.Length.Should().BeInRange(1, 512,
                "each chunk is at most ChunkSamples=512 and non-empty"));
    }
}
