using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class GenerateScreenFollowUpHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly string[] ThreadSafeAddition = ["make it thread-safe"];

    private static async IAsyncEnumerable<string> Stream(params string[] chunks)
    {
        foreach (var c in chunks) { yield return c; await Task.Yield(); }
    }

    // Builds a session whose only turn is the capture card "A" with a completed answer.
    private static (Session session, ConversationTurn cardA) SessionWithCapture()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var a = session.AddConversationTurn(q.Id, "Implement LRU cache", T0).Value;
        a.TransitionTo(ConversationTurnStatus.GeneratingRefined);
        a.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, "class LruCache {}", T0));
        a.TransitionTo(ConversationTurnStatus.RefinedReady);
        return (session, a);
    }

    private static (GenerateScreenFollowUpHandler handler, IConversationTurnSink turnSink,
                    IAnswerStreamSink streamSink, ScreenTaskContextStore store, IAnswerProvider provider)
        Make(Session session)
    {
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stream("updated ", "solution"));
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Base, AnswerSettings.Default, CodeProfile.Empty,
            MicDeviceId: null, LoopbackDeviceId: null)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));
        var store = new ScreenTaskContextStore();

        var handler = new GenerateScreenFollowUpHandler(
            repo, resolver, settings, streamSink, turnSink, uow, store, TimeProvider.System);
        return (handler, turnSink, streamSink, store, provider);
    }

    [Fact]
    public async Task Handle_CreatesNewCard_StreamsScreenFollowUp_DoesNotTouchCaptureCard()
    {
        var (session, cardA) = SessionWithCapture();
        var (handler, turnSink, streamSink, store, _) = Make(session);

        var result = await handler.Handle(new GenerateScreenFollowUpCommand(
            session.Id, cardA.Id, "Implement LRU cache", "Implement LRU cache",
            ScreenAnalysisMode.SolveCodingTask,
            ThreadSafeAddition, Array.Empty<string>()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().HaveCount(2, "a new follow-up card was added");
        cardA.AnswerVersions.Should().ContainSingle("the capture card is never mutated");

        var newCard = session.ConversationTurns.Single(t => t.Id != cardA.Id);
        newCard.AnswerVersions.Should().ContainSingle()
            .Which.VersionType.Should().Be(AnswerVersionType.ScreenFollowUp);
        turnSink.Received(1).OnTurnCreated(newCard.Id, "Implement LRU cache");
        store.Current.Should().BeNull("Register is the VM's job; the handler only sets latest card when a task exists");
    }

    [Fact]
    public async Task Handle_PassesPriorAnswerFromParent_IntoPrompt()
    {
        var (session, cardA) = SessionWithCapture();
        var (handler, _, _, _, provider) = Make(session);

        AnswerPrompt? captured = null;
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.ArgAt<AnswerPrompt>(0); return Stream("ok"); });

        await handler.Handle(new GenerateScreenFollowUpCommand(
            session.Id, cardA.Id, "Implement LRU cache", "Implement LRU cache",
            ScreenAnalysisMode.SolveCodingTask,
            ThreadSafeAddition, Array.Empty<string>()), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.User.Should().Contain("class LruCache {}");   // parent answer carried forward
    }
}
