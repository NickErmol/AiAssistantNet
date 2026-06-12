using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class AnswerLatestQuestionHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private static Session MakeSessionWith(params (Speaker Speaker, string Text, DateTimeOffset At)[] items)
    {
        // Real domain API (see GenerateAnswerHandlerTests / CreateScreenTurnHandlerTests):
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now.AddMinutes(-10)).Value;
        foreach (var (sp, text, at) in items)
            session.AddTranscriptItem(TranscriptItem.Create(sp, text, at, 1.0f));
        return session;
    }

    private sealed record Harness(
        AnswerLatestQuestionHandler Handler,
        ILatestQuestionExtractor Extractor,
        IConversationTurnSink TurnSink,
        IAnswerStreamSink StreamSink,
        IMediator Mediator);

    private static Harness NewHarness(Session session, int windowSeconds = 120)
    {
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DefaultSettings() with { LatestQuestionWindowSeconds = windowSeconds }));

        var extractor = Substitute.For<ILatestQuestionExtractor>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var streamSink = Substitute.For<IAnswerStreamSink>();
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute returns-setup: ValueTask is consumed by the framework
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new AnswerLatestQuestionHandler(
            repo, settings, extractor, turnSink, streamSink, mediator, uow,
            new FakeTimeProvider(Now));
        return new Harness(handler, extractor, turnSink, streamSink, mediator);
    }

    private static AppSettingsDto DefaultSettings() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty,
        null, null);

    [Fact]
    public async Task Found_CreatesTurn_AnnouncesIt_AndDelegatesToGenerate()
    {
        var session = MakeSessionWith(
            (Speaker.Other, "Hi", Now.AddSeconds(-100)),
            (Speaker.Me, "How would you shard a database?", Now.AddSeconds(-20)));
        var h = NewHarness(session);
        h.Extractor.ExtractAsync(Arg.Any<IReadOnlyList<TranscriptLine>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LatestQuestionResult(true, "How would you shard a database?", "DB scaling")));

        var result = await h.Handler.Handle(
            new AnswerLatestQuestionCommand(session.Id, []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().ContainSingle(t => t.InitialQuestionText == "How would you shard a database?");
        h.TurnSink.Received().OnTurnCreated(Arg.Any<ConversationTurnId>(), "How would you shard a database?");
        await h.Mediator.Received().Send(
            Arg.Is<GenerateAnswerCommand>(c => c.SessionId == session.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NotFound_CreatesDismissibleCard_WithError_NoGenerate()
    {
        var session = MakeSessionWith((Speaker.Me, "uh, hmm", Now.AddSeconds(-10)));
        var h = NewHarness(session);
        h.Extractor.ExtractAsync(Arg.Any<IReadOnlyList<TranscriptLine>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(LatestQuestionResult.None));

        var result = await h.Handler.Handle(
            new AnswerLatestQuestionCommand(session.Id, []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        h.TurnSink.Received().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
        await h.StreamSink.Received().OnErrorAsync(Arg.Any<ConversationTurnId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await h.Mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyWindow_DoesNotCallExtractor_AndReportsNoQuestion()
    {
        // Only an OLD item, outside the 120s window.
        var session = MakeSessionWith((Speaker.Me, "old question?", Now.AddSeconds(-600)));
        var h = NewHarness(session);

        var result = await h.Handler.Handle(
            new AnswerLatestQuestionCommand(session.Id, []), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await h.Extractor.DidNotReceive().ExtractAsync(
            Arg.Any<IReadOnlyList<TranscriptLine>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await h.StreamSink.Received().OnErrorAsync(Arg.Any<ConversationTurnId>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Captures_ArePassedToExtractor_AsLabeledScreenContext()
    {
        var session = MakeSessionWith((Speaker.Me, "design a cache", Now.AddSeconds(-15)));
        var h = NewHarness(session);
        string? seenScreen = null;
        h.Extractor.ExtractAsync(Arg.Any<IReadOnlyList<TranscriptLine>>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(ci => { seenScreen = ci.ArgAt<string?>(1); return Task.FromResult(LatestQuestionResult.None); });

        await h.Handler.Handle(
            new AnswerLatestQuestionCommand(session.Id,
                [new RecentCapture("30s ago", "class Cache {}")]), CancellationToken.None);

        seenScreen.Should().NotBeNull();
        seenScreen.Should().Contain("30s ago").And.Contain("class Cache {}");
    }
}
