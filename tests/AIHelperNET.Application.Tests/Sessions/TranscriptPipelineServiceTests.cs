using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
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

namespace AIHelperNET.Application.Tests.Sessions;

public class TranscriptPipelineServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;

    private static TranscriptItem MakeItem(Speaker speaker, string text)
        => TranscriptItem.Create(speaker, text, Now, 0.9f);

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow)
        MakeSvc(ITranscriptSink sink)
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var turnSink = Substitute.For<IConversationTurnSink>();

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        return (new TranscriptPipelineService(factory, sink, turnSink), mediator, turnSink, uow);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);

        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_NoTurnCreated()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", Now).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, uow, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task TranscriptSink_CalledForEveryItem()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_NotifiesConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        var expectedId = session.ConversationTurns[0].Id;
        turnSink.Received(1).OnTurnCreated(expectedId, item.Text);
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_DoesNotNotifyConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        turnSink.DidNotReceive().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
    }
}
