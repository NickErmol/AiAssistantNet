using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
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
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T0Plus4s = T0.AddSeconds(4); // triggers accumulator flush

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

    private static TranscriptItem MakeItem(Speaker speaker, string text, DateTimeOffset? at = null)
        => TranscriptItem.Create(speaker, text, at ?? T0, 0.9f);

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow)
        MakeSvc(ITranscriptSink sink, IQuestionClassifier? classifier = null)
    {
        // Default classifier: NewQuestion for any text that passes pre-filter
        if (classifier is null)
        {
            classifier = Substitute.For<IQuestionClassifier>();
            classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(ClassificationResult.NewQuestion));
        }

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

        return (new TranscriptPipelineService(factory, sink, turnSink, classifier), mediator, turnSink, uow);
    }

    // Helper: process a segment, then flush the accumulator (simulates the 3s gap firing)
    private static async Task ProcessAndFlushAsync(
        TranscriptPipelineService svc, Session session, TranscriptItem item, IUnitOfWork uow)
    {
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);
        await svc.FlushAccumulatorAsync(session, uow, CancellationToken.None);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await ProcessAndFlushAsync(svc, session, item, uow);

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
        await ProcessAndFlushAsync(svc, session, item, uow);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsNotAQuestion_NoTurnCreated()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.NotAQuestion));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you approach this problem?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsContinuation_WithActiveTurn_AppendsAndRegenerates()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var classifier = Substitute.For<IQuestionClassifier>();
        // First call: NewQuestion (creates the turn)
        // Second call: Continuation (appends to turn)
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(ClassificationResult.NewQuestion),
                Task.FromResult(ClassificationResult.Continuation));
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink, classifier);

        // First segment creates a turn
        var first = MakeItem(Speaker.Other, "Can we use observables and what is");
        await ProcessAndFlushAsync(svc, session, first, uow);
        session.ConversationTurns.Should().HaveCount(1);

        // Second segment is a continuation — should append, not create a new turn
        var second = MakeItem(Speaker.Other, "the difference comparing them to promise");
        await ProcessAndFlushAsync(svc, session, second, uow);

        session.ConversationTurns.Should().HaveCount(1, "Continuation must not create a new turn");
        session.ConversationTurns[0].InitialQuestionText.Should()
            .Contain("the difference comparing them to promise");

        await Task.Delay(200);
        // Two GenerateAnswerCommand calls: once for NewQuestion, once for Continuation
        await mediator.Received(2).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsContinuation_NoActiveTurn_PromotesToNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.Continuation));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you approach this kind of problem?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        // No active turn → Continuation promoted to NewQuestion
        session.ConversationTurns.Should().HaveCount(1);
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierThrows_FallsBackToNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClassificationResult>>(_ => throw new HttpRequestException("network error"));
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        // Falls back: QuestionDetector said yes, so treat as NewQuestion
        session.ConversationTurns.Should().HaveCount(1);
        await Task.Delay(200);
        await mediator.Received(1).Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoSegmentsWithin3s_CombinedAndClassifiedTogether()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        string? capturedText = null;
        classifier.ClassifyAsync(Arg.Do<string>(t => capturedText = t),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.NewQuestion));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        // Two segments 1s apart — accumulator should combine them
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "Can we use them", T0), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "and what is", T0.AddSeconds(1)), uow, CancellationToken.None);
        await svc.FlushAccumulatorAsync(session, uow, CancellationToken.None);

        capturedText.Should().Be("Can we use them and what is");
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", T0).Value;
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

        var item = MakeItem(Speaker.Me, "Hello there friend");
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
        await ProcessAndFlushAsync(svc, session, item, uow);

        var expectedId = session.ConversationTurns[0].Id;
        turnSink.Received(1).OnTurnCreated(expectedId, Arg.Any<string>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_DoesNotNotifyConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await ProcessAndFlushAsync(svc, session, item, uow);

        turnSink.DidNotReceive().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
    }

    // ── Boundary-detection path (boundaryClassifier != null) ────────────────

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow)
        MakeSvcWithBoundary(ITranscriptSink sink, IQuestionBoundaryClassifier boundaryClassifier)
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

        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier),
            mediator, turnSink, uow);
    }

    [Fact]
    public async Task BoundaryPath_QuestionStarted_CreatesTurnWithCollectingStatus()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var boundaryClassifier = Substitute.For<IQuestionBoundaryClassifier>();
        boundaryClassifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionStarted, 0.90,
                ShouldGenerateAnswer: false,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: "Let's say we have a payment service.",
                Reason: "test")));

        var (svc, mediator, _, uow) = MakeSvcWithBoundary(transcriptSink, boundaryClassifier);

        var item = MakeItem(Speaker.Other, "Let's say we have a payment service.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.CollectingQuestion);

        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_QuestionComplete_TransitionsTurnAndFiresAnswer()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Heuristic already returns high-confidence for both — no AI fallback needed
        // First call: QuestionStarted (heuristic returns high confidence)
        // Second call: QuestionComplete (heuristic returns high confidence)
        // We don't need a real boundary classifier since the heuristic in the
        // service will handle "Let's say…" and "What is DDD?" correctly.
        // Use the real heuristic by NOT supplying a boundaryClassifier,
        // but we need the boundary path, so supply a classifier that is never called
        // (high confidence heuristic results skip AI).
        var neverCalledClassifier = Substitute.For<IQuestionBoundaryClassifier>();
        neverCalledClassifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("fallback")));

        var (svc, mediator, _, uow) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

        // First item: scenario setup → QuestionStarted
        var first = MakeItem(Speaker.Other, "Let's say we have a payment service.");
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);
        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.CollectingQuestion);

        // Second item: complete question — must be ≥4 words so Rule 2 doesn't fire Unrelated
        var second = MakeItem(Speaker.Other, "What exactly is DDD?");
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "QuestionComplete should reuse the collecting turn");
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);

        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_AdditionalRequirement_FiresRefinement()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Set up a preliminary-ready turn manually so the second item has an active turn
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        // Heuristic will return AdditionalRequirement for "Also assume…" with PreliminaryReady
        var neverCalledClassifier = Substitute.For<IQuestionBoundaryClassifier>();
        neverCalledClassifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("fallback")));

        var (svc, mediator, _, uow) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

        var item = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_MeSpeaker_ClarificationNoAnswer()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Set up a preliminary-ready turn
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        // Heuristic: Speaker.Me + active turn → ClarificationOfCurrentQuestion (Rule 4, confidence 0.85)
        var neverCalledClassifier = Substitute.For<IQuestionBoundaryClassifier>();
        neverCalledClassifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("fallback")));

        var (svc, mediator, _, uow) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, uow, CancellationToken.None);

        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }
}
