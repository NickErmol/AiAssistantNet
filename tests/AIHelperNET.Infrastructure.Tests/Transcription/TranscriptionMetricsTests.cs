using AIHelperNET.Infrastructure.Transcription;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public sealed class TranscriptionMetricsTests
{
    [Fact]
    public void WindowAudioSeconds_16kHzMono_ConvertsSampleCount()
    {
        // 16000 samples at 16 kHz == 1.0 second
        Assert.Equal(1.0f, TranscriptionMetrics.WindowAudioSeconds(16000));
        Assert.Equal(0.5f, TranscriptionMetrics.WindowAudioSeconds(8000));
    }

    [Fact]
    public void RealtimeFactor_SlowerThanRealtime_IsGreaterThanOne()
    {
        // 2000 ms to transcribe 1.0 s of audio -> RTF 2.0
        Assert.Equal(2.0, TranscriptionMetrics.RealtimeFactor(2000, 1.0f), precision: 3);
    }

    [Fact]
    public void RealtimeFactor_FasterThanRealtime_IsLessThanOne()
    {
        Assert.Equal(0.5, TranscriptionMetrics.RealtimeFactor(500, 1.0f), precision: 3);
    }

    [Fact]
    public void RealtimeFactor_ZeroAudio_ReturnsZeroInsteadOfDivideByZero()
    {
        Assert.Equal(0.0, TranscriptionMetrics.RealtimeFactor(100, 0f));
    }
}
