using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class VoiceActivityDetector
{
    private const float EnergyThreshold = 0.01f;
    private const int SilenceFramesToFlush = 20;
    private const int MinFramesForSpeech = 5;

    public static async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new List<float>();
        var lastSpeaker = Speaker.Other;
        int silenceCount = 0;
        int speechCount = 0;

        await foreach (var frame in frames.WithCancellation(ct))
        {
            var energy = frame.Samples.Select(s => s * s).DefaultIfEmpty().Average();
            bool isSpeech = energy > EnergyThreshold;

            if (isSpeech)
            {
                buffer.AddRange(frame.Samples);
                lastSpeaker = frame.Speaker;
                speechCount++;
                silenceCount = 0;
            }
            else if (buffer.Count > 0)
            {
                silenceCount++;
                if (silenceCount >= SilenceFramesToFlush && speechCount >= MinFramesForSpeech)
                {
                    yield return new SpeechWindow([.. buffer], lastSpeaker);
                    buffer.Clear();
                    speechCount = 0;
                    silenceCount = 0;
                }
                else if (silenceCount >= SilenceFramesToFlush)
                {
                    buffer.Clear();
                    speechCount = 0;
                    silenceCount = 0;
                }
            }
        }

        if (buffer.Count > 0 && speechCount >= MinFramesForSpeech)
            yield return new SpeechWindow([.. buffer], lastSpeaker);
    }
}

public sealed record SpeechWindow(float[] Samples, Speaker Speaker);
