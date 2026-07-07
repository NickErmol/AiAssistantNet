using System.Diagnostics;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Security;
using AIHelperNET.Infrastructure.Transcription;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using AIHelperNET.Integration.Tests.E2E;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>
/// Opt-in live eval for the Deepgram streaming transcription path. Replays a committed WAV
/// fixture through <see cref="WavFileAudioCaptureService"/> into the real
/// <see cref="DeepgramTranscriptionService"/> (real WebSocket factory + real glossary provider)
/// and asserts the resulting transcript contains the expected phrase.
///
/// <para>Self-skips (passes trivially) when no Deepgram API key is stored in Windows
/// Credential Manager (target <c>AIHelperNET:DeepgramApiKey</c>), so CI and offline runs stay
/// green and never touch the network.</para>
/// </summary>
[Trait("Category", "LiveStt")]
public class DeepgramLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Deepgram_StreamsFixtureWav_ProducesExpectedTranscript_Fast()
    {
        // ── Guard: skip if no API key ──────────────────────────────────────────────
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey(SecretKind.Deepgram))
        {
            output.WriteLine("Skipped: no Deepgram API key in Windows Credential Manager " +
                "(target 'AIHelperNET:DeepgramApiKey').");
            return;
        }

        var capture = new WavFileAudioCaptureService(
            [new WavUtterance(Speaker.Other, "other_di.wav", GapMsBefore: 0)]);
        var sut = new DeepgramTranscriptionService(
            new DeepgramClientWebSocketFactory(), secrets, new JsonTranscriptionGlossaryProvider());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sw = Stopwatch.StartNew();
        var segments = new List<TranscriptSegment>();
        await foreach (var seg in sut.TranscribeAsync(
            capture.CaptureAsync(new AudioDeviceSelection(null, null), cts.Token),
            new TranscriptionOptions(WhisperModelSize.LargeTurbo, "en", new HashSet<string>()),
            cts.Token))
        {
            output.WriteLine($"[{sw.ElapsedMilliseconds} ms] conf={seg.Confidence:F2} {seg.Text}");
            segments.Add(seg);
        }

        segments.Should().NotBeEmpty("Deepgram should produce at least one endpointed utterance");
        var transcript = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
        transcript.Should().Contain("dependency injection");
        segments.All(s => s.Confidence is > 0f and <= 1f).Should().BeTrue();
    }
}
