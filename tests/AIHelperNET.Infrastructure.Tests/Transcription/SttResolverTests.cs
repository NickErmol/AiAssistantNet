using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Transcription;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class SttResolverTests
{
    private static (SttResolver sut, ITranscriptionService whisper, ISecretStore secrets,
        IOverlayStatusNotifier notifier) Make(bool hasDeepgramKey)
    {
        // The resolver only routes; both services can be stand-ins.
        var whisper = new FrameRecordingStt();
        var deepgram = new FrameRecordingStt();
        var secrets = Substitute.For<ISecretStore>();
        secrets.HasApiKey(SecretKind.Deepgram).Returns(hasDeepgramKey);
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        return (new SttResolver(whisper, deepgram, secrets, notifier), whisper, secrets, notifier);
    }

    [Fact]
    public void Whisper_ReturnsWhisperDirectly_AndClearsNotice()
    {
        var (sut, whisper, _, notifier) = Make(hasDeepgramKey: true);
        sut.Resolve(SttProvider.Whisper).Should().BeSameAs(whisper);
        notifier.Received(1).Notify(string.Empty);
    }

    [Fact]
    public void Deepgram_WithKey_ReturnsResilientWrapper()
    {
        var (sut, _, _, notifier) = Make(hasDeepgramKey: true);
        sut.Resolve(SttProvider.Deepgram).Should().BeOfType<ResilientTranscriptionService>();
        notifier.Received(1).Notify(string.Empty);
    }

    [Fact]
    public void Deepgram_WithoutKey_FallsBackToWhisper_AndNotifies()
    {
        var (sut, whisper, _, notifier) = Make(hasDeepgramKey: false);
        sut.Resolve(SttProvider.Deepgram).Should().BeSameAs(whisper);
        notifier.Received(1).Notify(Arg.Is<string>(m => m.Contains("no Deepgram key")));
    }
}
