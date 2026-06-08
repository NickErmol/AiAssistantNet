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

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback)
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

        var feedback = new TurnStatusFeedback();
        return (new TranscriptPipelineService(factory, sink, turnSink, classifier, feedback: feedback),
            mediator, turnSink, uow, feedback);
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
        var (svc, mediator, _, uow, _) = MakeSvc(transcriptSink);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink, classifier);

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
        var (svc, mediator, _, uow, _) = MakeSvc(transcriptSink, classifier);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink, classifier);

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
        var (svc, mediator, _, uow, _) = MakeSvc(transcriptSink, classifier);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink, classifier);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink);

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
        var (svc, _, _, uow, _) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello there friend");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_NotifiesConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow, _) = MakeSvc(transcriptSink);

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
        var (svc, _, turnSink, uow, _) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await ProcessAndFlushAsync(svc, session, item, uow);

        turnSink.DidNotReceive().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
    }

    // ── Boundary-detection path (boundaryClassifier != null) ────────────────

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback)
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

        var feedback = new TurnStatusFeedback();
        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier, feedback),
            mediator, turnSink, uow, feedback);
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

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, boundaryClassifier);

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

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

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

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

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

        // Rule 4 confidence is 0.50 for answered turns → AI classifier is called.
        // Classifier correctly identifies "Should it cover all error types?" as clarification.
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.ClarificationOfCurrentQuestion, 0.85,
                ShouldGenerateAnswer: false,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "Should it cover all error types?",
                Reason: "clarification")));

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, uow, CancellationToken.None);

        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    // ── Regression: multiple Speaker.Me questions fire independent turns ────────
    // Root cause: the pipeline holds one in-memory Session; GenerateAnswerHandler runs in a
    // separate DI scope and never propagates status changes back. Turns stay at Detected
    // in-memory forever. Before the fix, Rule 4 fired at 0.85 confidence for Detected turns,
    // skipped the AI classifier, and swallowed every subsequent question as a clarification.

    // ── Regression: a completed (Detected) turn must not swallow new interviewer questions ──
    // The pipeline's in-memory turn is stuck at Detected (the answer handler runs in a separate
    // DI scope and never propagates PreliminaryReady back). When the AI classifier returns a
    // folding label for a genuinely new interviewer question, routing must promote it to a new
    // turn instead of refining/dropping it — otherwise only the first card ever appears.

    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ClassifierReturnsClarification_OnCompletedTurn_CreatesNewTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Prior completed turn — Detected is exactly the stuck in-memory status after an answer.
        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddConversationTurn(q.Id, "Explain dependency injection.", T0);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.ClarificationOfCurrentQuestion, 0.85,
                ShouldGenerateAnswer: false,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "the system handles concurrent requests efficiently",
                Reason: "stale-context misclassification")));

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        // New interviewer question (no '?', not imperative) → heuristic is low-confidence → AI called.
        var item = MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2,
            "an interviewer utterance cannot clarify an already-completed turn — it is a new question");
        await Task.Delay(150);
        await mediator.Received(1).Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ClassifierReturnsContinuation_OnCompletedTurn_CreatesNewTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddConversationTurn(q.Id, "Explain dependency injection.", T0);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.85,
                ShouldGenerateAnswer: false,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "the system handles concurrent requests efficiently",
                Reason: "stale-context misclassification")));

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2,
            "a completed turn is not collecting fragments — a continuation of it is really a new question");
        await Task.Delay(150);
        await mediator.Received(1).Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    // ── Me utterances are deterministic (new model): no AI, never open a turn, never generate ──
    // Under the new conversation routing model, Speaker.Me utterances bypass the AI classifier
    // entirely and are routed by HandleMeUtterance. They attach context to the target turn only.
    // Me never opens a turn regardless of how question-like the text appears.

    [Fact]
    public async Task BoundaryPath_MeSpeaker_ClearQuestion_AttachesToExistingTurn_NoNewTurn_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Prior completed turn — Detected is the stuck in-memory status after an answer.
        var q = DetectedQuestion.Create("What is dependency injection in .NET?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is dependency injection in .NET?", T0).Value;

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var item = MakeItem(Speaker.Me, "What are EF Core proxies?", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1,
            "Me never opens a turn — it attaches to the existing turn instead");
        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification,
            "Detected turn flips to AwaitingClarification when Me speaks");
        turn.ClarificationQuestionIds.Should().Contain(item.Id);
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_MeSpeaker_AIReturnsNoQuestion_NonQuestionNoise_DoesNotCreateTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("What is dependency injection in .NET?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddConversationTurn(q.Id, "What is dependency injection in .NET?", T0);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("yeah I think so as well")));

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        // Ambiguous chatter that the bias-free heuristic also does NOT see as a question.
        var item = MakeItem(Speaker.Me, "yeah I think so as well", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1,
            "non-question chatter must still be dropped — the safety net only rescues clear questions");
        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_MultipleMe_NoTurns_AllHold_NoneCreatesATurn()
    {
        // New model: Me never opens a turn. With no prior turns (no Other questions yet),
        // multiple Me utterances all hold — none creates a turn, none generates.
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var q1 = MakeItem(Speaker.Me, "What's the difference between class and interface in .NET?", T0);
        var q2 = MakeItem(Speaker.Me, "What are the proxies in EF?", T0.AddSeconds(60));
        var q3 = MakeItem(Speaker.Me, "Tell me the difference between static constructor and private constructor.", T0.AddSeconds(120));

        await svc.ProcessAsync(session, q1, uow, CancellationToken.None);
        await svc.ProcessAsync(session, q2, uow, CancellationToken.None);
        await svc.ProcessAsync(session, q3, uow, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty("Me never opens a turn");
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_MultipleMe_WithExistingTurn_AllAttachAsContext_NoNewTurns()
    {
        // New model: Me never opens a turn. With an existing Other-created turn (Detected),
        // the first Me flips it to AwaitingClarification; subsequent Me utterances continue to
        // attach context but do not open new turns and do not generate.
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var q1 = MakeItem(Speaker.Me, "What's the difference between class and interface in .NET?", T0);
        var q2 = MakeItem(Speaker.Me, "What are the proxies in EF?", T0.AddSeconds(60));
        var q3 = MakeItem(Speaker.Me, "Tell me the difference between static constructor and private constructor.", T0.AddSeconds(120));

        await svc.ProcessAsync(session, q1, uow, CancellationToken.None);
        await svc.ProcessAsync(session, q2, uow, CancellationToken.None);
        await svc.ProcessAsync(session, q3, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "Me never opens a new turn");
        turn.ClarificationQuestionIds.Should().HaveCount(3, "all three Me items attach as context");
        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_TwoDistinctQuestions_DoNotCancelEachOther()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Capture the cancellation token each GenerateAnswerCommand was dispatched with.
        // ConcurrentDictionary used as an ordered accumulator so token capture is thread-safe
        // across the two fire-and-forget Task.Run callbacks.
        var capturedTokens = new System.Collections.Concurrent.ConcurrentQueue<CancellationToken>();
        var neverCalled = Substitute.For<IQuestionBoundaryClassifier>();
        neverCalled.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("fallback")));

        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, neverCalled);
        // Use When/Do to capture tokens as a side-effect; this runs regardless of Returns
        // sequencing and does not interfere with the pre-configured Ok() return value.
        mediator.When(m => m.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>()))
            .Do(ci => capturedTokens.Enqueue(ci.ArgAt<CancellationToken>(1)));

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "What exactly is dependency injection?"), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "What exactly is the repository pattern?"), uow, CancellationToken.None);
        await Task.Delay(200);

        var tokens = capturedTokens.ToArray();
        session.ConversationTurns.Should().HaveCount(2);
        tokens.Should().HaveCount(2);
        tokens[0].IsCancellationRequested.Should().BeFalse("a second distinct question must not cancel the first");
    }

    [Fact]
    public async Task BoundaryPath_Me_UnansweredTurn_AttachesClarificationAndAwaits_NoAi_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value; // status Detected (unanswered)

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Do they mean constructor injection specifically?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "Me must never open a turn");
        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().Contain(me.Id);
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_Me_AnsweredTurn_RecordsContextOnly_NoStatusChange_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // already answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Actually also cover keyed services.");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady, "answered-turn follow-up does not change status");
        turn.ClarificationQuestionIds.Should().Contain(me.Id);
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_Me_NoTurns_Holds_NoTurnCreated_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Wait, what do they mean?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_FeedbackPreliminaryReady_UnlocksAdditionalRequirementRefine()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Seed a turn that the pipeline created and that the (separate-scope) answer handler has
        // since advanced to PreliminaryReady — delivered via the feedback channel, NOT a direct mutation.
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        // turn is still Detected in the pipeline's in-memory copy.

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.AdditionalRequirement, 0.95,
                ShouldGenerateAnswer: true, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "Also assume it's a web app.", Reason: "test")));

        var (svc, mediator, _, uow, feedback) = MakeSvcWithBoundary(transcriptSink, classifier);

        // The answer worker reports the turn reached PreliminaryReady.
        feedback.Publish(new TurnStatusEvent(turn.Id, ConversationTurnStatus.PreliminaryReady));

        var item = MakeItem(Speaker.Other, "Also assume it's a web app.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        // Drain was applied → in-memory status is now PreliminaryReady, so Rule 8 regenerates.
        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id
                && c.VersionType == AnswerVersionType.Preliminary),
            Arg.Any<CancellationToken>());
    }
}
