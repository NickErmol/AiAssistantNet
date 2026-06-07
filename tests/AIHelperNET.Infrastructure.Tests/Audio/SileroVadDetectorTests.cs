using System.Net.Http;
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Audio;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Transcription;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Audio;

public sealed class SileroVadDetectorTests
{
    private static string ModelPath => System.IO.Path.Combine(AppPaths.ModelsDir, "silero_vad.onnx");

    private static SileroModelProvider MakeProvider()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        return new SileroModelProvider(factory);
    }

    [SkippableFact]
    public async Task GetSessionAsync_ReturnsSession_WhenModelExists()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();
        var session = await provider.GetSessionAsync(CancellationToken.None);
        Assert.NotNull(session);
        await provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task SilenceAudio_ProducesNoWindows()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();

        // 3 seconds of silence at 16 kHz
        var silence = new float[16000 * 3];
        var frames = new[] { new AudioFrame(silence, Speaker.Other, DateTimeOffset.UtcNow) };

        var windows = new List<SpeechWindow>();
        await foreach (var w in SileroVadDetector.AccumulateSpeechWindows(
            frames.AsAsync(), provider, CancellationToken.None))
        {
            windows.Add(w);
        }

        Assert.Empty(windows);
        await provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task GetSessionAsync_ReturnsSameInstance_OnSecondCall()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();
        var s1 = await provider.GetSessionAsync(CancellationToken.None);
        var s2 = await provider.GetSessionAsync(CancellationToken.None);
        Assert.Same(s1, s2);
        await provider.DisposeAsync();
    }
}

file static class AsyncExtensions
{
    public static async IAsyncEnumerable<T> AsAsync<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
