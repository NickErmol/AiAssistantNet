using System.IO;
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using NAudio.Wave;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>One scripted audio utterance sourced from a committed WAV fixture.</summary>
/// <param name="Speaker">Channel the utterance is emitted on (Me=mic, Other=loopback).</param>
/// <param name="WavFileName">Fixture file name under Fixtures/audio/.</param>
/// <param name="GapMsBefore">Real wall-clock delay before this utterance's frames, to drive
/// SessionRunner's same-speaker merge window between utterances.</param>
public sealed record WavUtterance(Speaker Speaker, string WavFileName, int GapMsBefore);

/// <summary>
/// Test <see cref="IAudioCaptureService"/> that replays committed WAV fixtures as real 16 kHz
/// mono float <see cref="AudioFrame"/>s, tagged per <see cref="WavUtterance.Speaker"/>, then
/// completes the stream so the SessionRunner loop terminates. Feeds frames in ~32 ms chunks at
/// real time so the Silero VAD and the merge window behave as in production.
/// </summary>
public sealed class WavFileAudioCaptureService(IReadOnlyList<WavUtterance> script) : IAudioCaptureService
{
    private const int ChunkSamples = 512; // 32 ms at 16 kHz — matches VAD chunk size

    /// <summary>Absolute path to the committed fixtures directory in the test output.</summary>
    public static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "audio");

    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var utt in script)
        {
            if (utt.GapMsBefore > 0)
                await Task.Delay(utt.GapMsBefore, ct);

            var samples = ReadMono16k(Path.Combine(FixtureDir, utt.WavFileName));
            var now = DateTimeOffset.UtcNow;

            for (var offset = 0; offset < samples.Length; offset += ChunkSamples)
            {
                var len = Math.Min(ChunkSamples, samples.Length - offset);
                var chunk = samples[offset..(offset + len)];
                yield return new AudioFrame(chunk, utt.Speaker, now);
                // Pace at ~real time so VAD silence timing and the merge window are realistic.
                await Task.Delay(TimeSpan.FromMilliseconds(32), ct);
            }
        }
    }

    /// <summary>Reads a WAV file as 16 kHz mono float samples (fixtures are already 16 kHz mono).</summary>
    private static float[] ReadMono16k(string path)
    {
        using var reader = new AudioFileReader(path); // exposes float samples via Read(float[])
        var all = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate]; // 1 s chunks
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer[..read]);
        return [.. all];
    }
}
