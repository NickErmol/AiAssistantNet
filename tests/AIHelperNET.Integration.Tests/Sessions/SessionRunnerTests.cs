using System.Runtime.CompilerServices;
using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Integration.Tests.Sessions;

public class SessionRunnerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;

    private static IServiceScopeFactory MakeScopeFactory(Session session)
    {
        var mediator = Substitute.For<IMediator>();

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);
        provider.GetService(typeof(ISessionRepository)).Returns(repo);
        provider.GetService(typeof(IUnitOfWork)).Returns(uow);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        return factory;
    }

    private static SessionRunner MakeRunner(Session session, IEnumerable<AudioFrame> captureFrames)
    {
        var scopeFactory   = MakeScopeFactory(session);
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var turnSink       = Substitute.For<IConversationTurnSink>();
        var pipeline       = new TranscriptPipelineService(scopeFactory, transcriptSink, turnSink);
        var capture        = new FakeAudioCaptureService(captureFrames);
        var transcription  = new FakeTranscriptionService();
        return new SessionRunner(scopeFactory, capture, transcription, pipeline);
    }

    [Fact]
    public async Task BothMode_MicAndLoopbackFrames_BothEndInTranscript()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames);

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.Both);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Other);
    }

    [Fact]
    public async Task MicrophoneOnlyMode_LoopbackFrameDropped()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames);

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.MicrophoneOnly);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().NotContain(i => i.Speaker == Speaker.Other);
    }

    [Fact]
    public async Task SystemAudioOnlyMode_MicFrameDropped()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames);

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.SystemAudioOnly);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().NotContain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Other);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeAudioCaptureService(IEnumerable<AudioFrame> source)
        : IAudioCaptureService
    {
        public async IAsyncEnumerable<AudioFrame> CaptureAsync(
            AudioDeviceSelection selection,
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var frame in source)
            {
                ct.ThrowIfCancellationRequested();
                yield return frame;
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Passes each AudioFrame through as a TranscriptSegment — no VAD or Whisper.
    /// Allows verifying speaker routing without real audio processing.
    /// </summary>
    private sealed class FakeTranscriptionService : ITranscriptionService
    {
        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            IAsyncEnumerable<AudioFrame> frames,
            WhisperModelSize model,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var frame in frames.WithCancellation(ct))
            {
                yield return new TranscriptSegment(
                    $"text-{frame.Speaker}", frame.Speaker, frame.CapturedAt, 1.0f);
            }
        }
    }
}
