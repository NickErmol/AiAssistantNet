using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class DeepgramTranscriptionServiceTests
{
    // Triple-dollar raw string: see DeepgramParsingTests.cs for why $$$ is needed here
    // (a run of literal closing braces at the end of the payload).
    private static string ResultsJson(string transcript, float conf, bool speechFinal, double start) => $$$"""
        {"type":"Results","start":{{{start.ToString(CultureInfo.InvariantCulture)}}},"is_final":true,
         "speech_final":{{{(speechFinal ? "true" : "false")}}},
         "channel":{"alternatives":[{"transcript":"{{{transcript}}}","confidence":{{{conf.ToString(CultureInfo.InvariantCulture)}}}}]}}
        """;

    private static DeepgramTranscriptionService MakeSut(
        FakeDeepgramSocket socket, TimeSpan? keepAlive = null)
    {
        var secrets = Substitute.For<ISecretStore>();
        var key = new SecureString();
        foreach (var c in "dg-fake-key") key.AppendChar(c);
        key.MakeReadOnly();
        secrets.GetApiKey(SecretKind.Deepgram).Returns(Result.Ok(key));

        var glossary = Substitute.For<ITranscriptionGlossaryProvider>();
        glossary.Domains.Returns([]);

        return new DeepgramTranscriptionService(
            new FakeDeepgramSocketFactory(socket), secrets, glossary, keepAlive);
    }

    private static TranscriptionOptions Options(string language = "auto")
        => new(WhisperModelSize.LargeTurbo, language, new HashSet<string>());

    private static async IAsyncEnumerable<AudioFrame> Frames(
        Speaker speaker, int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new AudioFrame(new float[512], speaker, DateTimeOffset.UtcNow);
            await Task.Yield();
        }
    }

    private static async Task<List<TranscriptSegment>> Collect(
        IAsyncEnumerable<TranscriptSegment> stream)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var s in stream) list.Add(s);
        return list;
    }

    [Fact]
    public async Task EmitsOneSegmentPerSpeechFinal_WithSpeakerAndConfidence()
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("how would you implement dependency injection", 0.97f, true, 1.2));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 3), Options(), CancellationToken.None));

        segments.Should().ContainSingle();
        segments[0].Text.Should().Be("how would you implement dependency injection");
        segments[0].Speaker.Should().Be(Speaker.Other);
        segments[0].Confidence.Should().BeApproximately(0.97f, 0.001f);
    }

    [Fact]
    public async Task JoinsChunksAcrossResultsUntilSpeechFinal()
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("tell me about your experience with", 0.9f, false, 0.5));
        socket.EnqueueMessage(ResultsJson("event driven architecture", 0.8f, true, 3.1));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 2), Options(), CancellationToken.None));

        segments.Should().ContainSingle();
        segments[0].Text.Should().Be("tell me about your experience with event driven architecture");
    }

    [Fact]
    public async Task DropsUtterancesBelowMinWords()  // parity with the Whisper path's MinWords = 3
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("okay great", 0.99f, true, 0.1));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 1), Options(), CancellationToken.None));

        segments.Should().BeEmpty();
    }

    [Fact]
    public async Task SendsAudioAsLinear16_ThenCloseStreamWhenInputEnds()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 2), Options(), CancellationToken.None));

        socket.SentAudio.Should().HaveCount(2);
        socket.SentAudio[0].Should().HaveCount(1024);           // 512 floats -> 1024 bytes
        socket.SentText.Should().Contain(t => t.Contains("CloseStream"));
    }

    [Fact]
    public async Task BuildsUri_WithProtocolParams_AndOmitsLanguageForAuto()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 1), Options("auto"), CancellationToken.None));

        var q = socket.ConnectedUri!.Query;
        q.Should().Contain("model=nova-3").And.Contain("encoding=linear16")
         .And.Contain("sample_rate=16000").And.Contain("channels=1")
         .And.Contain("smart_format=true").And.Contain("interim_results=false")
         .And.Contain("endpointing=300");
        q.Should().NotContain("language=");
        socket.ApiKey.Should().Be("dg-fake-key");
    }

    [Fact]
    public async Task BuildsUri_WithExplicitLanguage()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 1), Options("en"), CancellationToken.None));

        socket.ConnectedUri!.Query.Should().Contain("language=en");
    }

    [Fact]
    public async Task SendsKeepAlive_WhenFramesGoIdle()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket, keepAlive: TimeSpan.FromMilliseconds(30));

        async IAsyncEnumerable<AudioFrame> SlowFrames([EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new AudioFrame(new float[512], Speaker.Me, DateTimeOffset.UtcNow);
            await Task.Delay(200, ct);
            yield return new AudioFrame(new float[512], Speaker.Me, DateTimeOffset.UtcNow);
        }

        await Collect(sut.TranscribeAsync(SlowFrames(), Options(), CancellationToken.None));

        socket.SentText.Should().Contain(t => t.Contains("KeepAlive"));
    }

    [Fact]
    public async Task EmptyFrameStream_YieldsNothing_WithoutConnecting()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        async IAsyncEnumerable<AudioFrame> Empty([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        var segments = await Collect(sut.TranscribeAsync(Empty(), Options(), CancellationToken.None));

        segments.Should().BeEmpty();
        socket.ConnectedUri.Should().BeNull();
    }
}

internal sealed class FakeDeepgramSocketFactory(FakeDeepgramSocket socket) : IDeepgramSocketFactory
{
    public IDeepgramSocket Create() => socket;
}

internal sealed class FakeDeepgramSocket : IDeepgramSocket
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();

    public Uri? ConnectedUri { get; private set; }
    public string? ApiKey { get; private set; }
    public List<byte[]> SentAudio { get; } = [];
    public List<string> SentText { get; } = [];

    public void EnqueueMessage(string json) => _incoming.Writer.TryWrite(json);

    public Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct)
    {
        ConnectedUri = uri;
        // Copy rather than alias: production zeroes its plaintext key buffer right after
        // connect (defence in depth), same as a real socket would copy it into a header.
        ApiKey = new string(apiKey.AsSpan());
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
    {
        SentAudio.Add(pcm.ToArray());
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string json, CancellationToken ct)
    {
        SentText.Add(json);
        // Server closes after CloseStream once queued results are drained (mirrors real behavior).
        if (json.Contains("CloseStream")) _incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        if (!await _incoming.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return null;
        return _incoming.Reader.TryRead(out var m) ? m : null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
