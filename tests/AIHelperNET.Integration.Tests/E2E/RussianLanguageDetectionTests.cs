using System.Text;
using System.Text.RegularExpressions;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.Transcription;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Live verification of the Whisper language setting over a real Russian-with-English-terms WAV
/// (ru_mixed.wav). Runs the REAL transcription service twice: language="auto" must auto-detect
/// Russian and emit Cyrillic; language="en" reproduces the old forced-English behaviour.
/// Tagged RealAudio so the fast suite excludes it. Uses the Base model (already on disk).
/// </summary>
[Trait("Category", "RealAudio")]
public sealed class RussianLanguageDetectionTests(ITestOutputHelper output) : IAsyncLifetime
{
    private const WhisperModelSize Model = WhisperModelSize.Base;
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static IAsyncEnumerable<AudioFrame> RussianFrames(CancellationToken ct) =>
        new WavFileAudioCaptureService([new WavUtterance(Speaker.Other, "ru_mixed.wav", GapMsBefore: 0)])
            .CaptureAsync(new AudioDeviceSelection("mic", "loopback"), ct);

    private async Task<string> TranscribeAsync(string language)
    {
        var svc = _host.Services.GetRequiredService<WhisperTranscriptionService>();
        var sb = new StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await foreach (var seg in svc.TranscribeAsync(
                           RussianFrames(cts.Token),
                           new TranscriptionOptions(Model, language, new HashSet<string>()), cts.Token))
            sb.Append(seg.Text).Append(' ');
        return sb.ToString().Trim();
    }

    private static bool HasCyrillic(string s) => Regex.IsMatch(s, "\\p{IsCyrillic}");

    [Fact]
    public async Task Auto_DetectsRussian_AndForcedEnglish_Does_Not()
    {
        var auto = await TranscribeAsync("auto");
        var en   = await TranscribeAsync("en");

        output.WriteLine($"[auto] {auto}");
        output.WriteLine($"[en  ] {en}");

        auto.Should().NotBeNullOrWhiteSpace("auto-detect should transcribe the Russian audio");
        HasCyrillic(auto).Should().BeTrue("auto-detect should yield Cyrillic for Russian speech");
        HasCyrillic(en).Should().BeFalse("forcing English must not produce Cyrillic");
    }
}
