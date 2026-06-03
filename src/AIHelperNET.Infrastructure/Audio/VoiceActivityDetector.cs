using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class VoiceActivityDetector
{
    private const float EnergyThreshold     = 0.005f; // sensitive enough for loopback/quiet mic
    private const int   SilenceFramesToFlush = 10;    // ~1 s pause triggers flush
    private const int   MinFramesForSpeech   = 20;   // ~1.2 s minimum speech — prevents tiny chunks

    public static async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer       = new List<float>();
        var lastSpeaker  = Speaker.Other;
        int silenceCount = 0;
        int speechCount  = 0;
        int totalFrames  = 0;
        int speechFrames = 0;

        await foreach (var frame in frames.WithCancellation(ct))
        {
            totalFrames++;
            var energy  = frame.Samples.Select(s => s * s).DefaultIfEmpty().Average();
            bool isSpeech = energy > EnergyThreshold;

            if (totalFrames % 200 == 1)
                Log.Debug("VAD: frame={F} energy={E:F5} speech={S} bufLen={B}", totalFrames, energy, isSpeech, buffer.Count);

            if (isSpeech)
            {
                buffer.AddRange(frame.Samples);
                lastSpeaker = frame.Speaker;
                speechCount++;
                speechFrames++;
                silenceCount = 0;
            }
            else if (buffer.Count > 0)
            {
                silenceCount++;
                if (silenceCount >= SilenceFramesToFlush && speechCount >= MinFramesForSpeech)
                {
                    Log.Information("VAD: emitting SpeechWindow speaker={S} samples={N} speechFrames={F}", lastSpeaker, buffer.Count, speechCount);
                    yield return new SpeechWindow([.. buffer], lastSpeaker);
                    buffer.Clear();
                    speechCount  = 0;
                    silenceCount = 0;
                }
                else if (silenceCount >= SilenceFramesToFlush)
                {
                    Log.Debug("VAD: discarding short window (speechCount={S} < min {M})", speechCount, MinFramesForSpeech);
                    buffer.Clear();
                    speechCount  = 0;
                    silenceCount = 0;
                }
            }
        }

        if (buffer.Count > 0 && speechCount >= MinFramesForSpeech)
        {
            Log.Information("VAD: flushing final SpeechWindow speaker={S} samples={N}", lastSpeaker, buffer.Count);
            yield return new SpeechWindow([.. buffer], lastSpeaker);
        }

        Log.Information("VAD: done — totalFrames={T} speechFrames={S}", totalFrames, speechFrames);
    }
}

public sealed record SpeechWindow(float[] Samples, Speaker Speaker);
