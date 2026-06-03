using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
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

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012 // NSubstitute setup — ValueTask is consumed by the Returns extension, not stored
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_NoTurnCreated()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        // Set up an active turn in PreliminaryReady status
        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", Now).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task TranscriptSink_CalledForEveryItem()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }
}
