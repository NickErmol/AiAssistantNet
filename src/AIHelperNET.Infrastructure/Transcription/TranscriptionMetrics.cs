namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>
/// Pure helpers for transcription timing diagnostics. No I/O — safe to unit test.
/// </summary>
public static class TranscriptionMetrics
{
    private const int SampleRate = 16000;

    /// <summary>Audio duration in seconds for a 16 kHz mono sample buffer.</summary>
    public static float WindowAudioSeconds(int sampleCount) => sampleCount / (float)SampleRate;

    /// <summary>
    /// Real-time factor: inference wall-time relative to the audio duration.
    /// A value &gt; 1 means transcription is slower than real time (the root of the
    /// "transcript appears N seconds late" symptom). Returns 0 for non-positive audio
    /// to avoid divide-by-zero.
    /// </summary>
    public static double RealtimeFactor(long inferMs, float windowAudioSec) =>
        windowAudioSec <= 0f ? 0d : inferMs / (windowAudioSec * 1000d);
}
