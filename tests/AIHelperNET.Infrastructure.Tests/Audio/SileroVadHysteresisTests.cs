using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Audio;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Audio;

public sealed class SileroVadHysteresisTests
{
    // Dummy 512-sample chunk — content irrelevant for hysteresis tests.
    private static float[] Chunk => new float[512];

    [Fact]
    public void AllSilence_EmitsNoWindows()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        for (int i = 0; i < 50; i++)
            Collect(acc, windows, 0.1f);

        var final = acc.Flush();
        if (final is not null) windows.Add(final);

        Assert.Empty(windows);
    }

    [Fact]
    public void ShortBurst_BelowMinChunks_IsDiscarded()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm chunks (triggers speech start, chunkCount=2)
        // + 4 speech chunks (chunkCount=6, below MinChunks=8)
        // + 12 silence chunks (SilenceFlushCount reached, discard)
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);  // confirm
        for (int i = 0; i < 4; i++) Collect(acc, windows, 0.9f);  // speech
        for (int i = 0; i < 12; i++) Collect(acc, windows, 0.1f); // silence

        Assert.Empty(windows);
    }

    [Fact]
    public void NormalSpeech_EmitsOneWindow()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm + 20 speech + 12 silence → one window
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < 20; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < 12; i++) Collect(acc, windows, 0.1f);

        Assert.Single(windows);
        Assert.Equal(Speaker.Other, windows[0].Speaker);
    }

    [Fact]
    public void MaxWindowReached_ForcesFlush()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm + (MaxChunks - 2) speech = exactly MaxChunks total → force-flush
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < VadWindowAccumulator.MaxChunks - 2; i++) Collect(acc, windows, 0.9f);

        Assert.Single(windows);
    }

    [Fact]
    public void NoisyOnset_AlternatingProbabilities_NeverStartsSpeech()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // alternating 0.4/0.6 — never 2 consecutive chunks ≥ 0.5
        for (int i = 0; i < 20; i++)
            Collect(acc, windows, i % 2 == 0 ? 0.4f : 0.6f);

        var final = acc.Flush();
        if (final is not null) windows.Add(final);

        Assert.Empty(windows);
    }

    private static void Collect(VadWindowAccumulator acc, List<SpeechWindow> windows, float prob)
    {
        var w = acc.Feed(prob, Chunk, Speaker.Other);
        if (w is not null) windows.Add(w);
    }
}
