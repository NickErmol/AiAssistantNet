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
using Microsoft.Extensions.Time.Testing;
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

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback, IBoundaryDecisionRecorder recorder)
        MakeSvcWithBoundary(ITranscriptSink sink, IQuestionBoundaryClassifier boundaryClassifier,
            TimeProvider? time = null, IBoundaryDecisionRecorder? recorder = null)
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
        var rec = recorder ?? Substitute.For<IBoundaryDecisionRecorder>();
        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier, feedback, time, rec),
            mediator, turnSink, uow, feedback, rec);
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

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, boundaryClassifier);

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

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier);

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

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier, time);

        var item = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMilliseconds(1100));
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

        // Me utterances bypass the AI classifier entirely — deterministic routing attaches them
        // as context to the current turn. The classifier setup below is intentionally absent;
        // the DidNotReceive assertion below proves it is never reached.
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, uow, CancellationToken.None);

        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
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
    public async Task BoundaryPath_OtherSpeaker_ClarificationOnAnsweredTurn_RefinesSameTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.ClarificationOfCurrentQuestion, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "specifically for high-throughput services", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "specifically for high-throughput services", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "an interviewer clarification refines the answered turn, not a new card");
        turn.InitialQuestionText.Should().Contain("specifically for high-throughput services");

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ContinuationOnAnsweredTurn_RefinesSameTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "it should also remain testable under heavy load", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "it should also remain testable under heavy load", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "a continuation refines the answered turn, not a new card");
        turn.InitialQuestionText.Should().Contain("it should also remain testable under heavy load");

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

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
    public async Task BoundaryPath_Me_NonQuestionNoise_AttachesAsContext_NoNewTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("What is dependency injection in .NET?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddConversationTurn(q.Id, "What is dependency injection in .NET?", T0);

        // Me utterances bypass the AI classifier entirely — HandleMeUtterance attaches the
        // utterance as context to the existing turn. No new turn is opened regardless of whether
        // the text looks like a question, because Me never opens a turn.
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var item = MakeItem(Speaker.Me, "yeah I think so as well", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        session.ConversationTurns.Should().HaveCount(1,
            "Me never opens a turn — it attaches as context to the existing turn");
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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

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

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, neverCalled);
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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

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
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Wait, what do they mean?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_Me_AllTurnsTerminal_Holds_NoAttach_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.Resolve(); // terminal — ActiveTurn is now null, only LastTurn remains (terminal)

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Wait, what did they mean?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        // A Me utterance must NOT resurrect a dismissed/resolved turn: no attach, no status change.
        turn.ClarificationQuestionIds.Should().BeEmpty("a Me utterance must not attach to a terminal turn");
        turn.Status.Should().Be(ConversationTurnStatus.Resolved);
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_QuestionContinued_AfterFire_BurstOfFragments_CoalescesToOneRegen()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // First "What exactly is DDD?" completes a question (high-confidence heuristic → fires).
        // Subsequent fragments are forced to QuestionContinued via the AI classifier.
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.95,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "frag", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        // Seed an answered turn (PreliminaryReady) the continuations will refine.
        var q = DetectedQuestion.Create("What exactly is DDD?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What exactly is DDD?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "the system handles concurrent requests efficiently"), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "especially under sustained heavy load"), uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "continuations refine the same turn");
        turn.InitialQuestionText.Should().Contain("especially under sustained heavy load");
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);

        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_QuestionContinued_TerminalTurn_OpensNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.Resolve(); // terminal

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.95,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "x", Reason: "test")));

        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(60)), uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "a continuation of a terminal turn is a new question");
    }

    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ClarificationOnCollectingTurn_OpensNewQuestion()
    {
        // Fix 1 regression: a ClarificationOfCurrentQuestion while the active turn is still
        // CollectingQuestion must NOT append+regen — it must fall through to HandleNewQuestion
        // and produce a second turn.
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Seed a CollectingQuestion turn (StartCollectingTurn transitions to CollectingQuestion).
        var q = DetectedQuestion.Create("Let's say we have a payment service.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var collectingTurnResult = session.StartCollectingTurn(q.Id, "Let's say we have a payment service.", T0);
        collectingTurnResult.IsSuccess.Should().BeTrue();
        var collectingTurn = collectingTurnResult.Value;
        collectingTurn.Status.Should().Be(ConversationTurnStatus.CollectingQuestion);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.ClarificationOfCurrentQuestion, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "the system handles concurrent requests efficiently",
                Reason: "test")));

        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        // Declarative, non-interrogative fragment — heuristic stays below 0.7 so mock label is used.
        var item = MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(2));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2,
            "a ClarificationOfCurrentQuestion while collecting is a new question, not an append");
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[1].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ContinuationOnDetectedTurn_RefinesSameTurn()
    {
        // Coverage gap: same shape as ContinuationOnAnsweredTurn but the turn is at Detected
        // status (added via AddConversationTurn without transitioning — that yields Detected).
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;
        // turn.Status is Detected — not further transitioned.
        turn.Status.Should().Be(ConversationTurnStatus.Detected);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "the system handles concurrent requests efficiently",
                Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "a continuation of a Detected turn refines it, not a new card");
        turn.InitialQuestionText.Should().Contain("the system handles concurrent requests efficiently");
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
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

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, feedback, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        // The answer worker reports the turn reached PreliminaryReady.
        feedback.Publish(new TurnStatusEvent(turn.Id, ConversationTurnStatus.PreliminaryReady));

        var item = MakeItem(Speaker.Other, "Also assume it's a web app.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        // Drain applied → in-memory status is PreliminaryReady, so the requirement refines this turn.
        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id
                && c.VersionType == AnswerVersionType.Preliminary),
            Arg.Any<CancellationToken>());
    }

    // ── AsrConfidenceGate integration tests ─────────────────────────────────

    [Fact]
    public async Task BoundaryPath_LowAsrConfidenceContinuation_IsDropped_NotFolded()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain write amplification.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain write amplification.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);
        var originalQuestion = turn.InitialQuestionText;

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.72,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "welcome through how you would add cash in",
                Reason: "test")));

        var (svc, mediator, _, uow, _, recorder) = MakeSvcWithBoundary(transcriptSink, classifier);

        var garbled = TranscriptItem.Create(
            Speaker.Other, "welcome through how you would add cash in without service day of data",
            T0.AddSeconds(1), 0.30f);
        await svc.ProcessAsync(session, garbled, uow, CancellationToken.None);

        turn.InitialQuestionText.Should().Be(originalQuestion);
        session.ConversationTurns.Should().HaveCount(1);

        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        recorder.Received().Record(Arg.Is<BoundaryDecisionRecord>(r => r.Route == "AsrDropped"));
    }

    [Fact]
    public async Task BoundaryPath_HighAsrConfidenceContinuation_StillFolds()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain write amplification.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain write amplification.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.72,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "and how does it affect SSD lifespan",
                Reason: "test")));

        var (svc, _, _, uow, _, recorder) = MakeSvcWithBoundary(transcriptSink, classifier);

        var clean = TranscriptItem.Create(
            Speaker.Other, "and how does it affect SSD lifespan over time", T0.AddSeconds(1), 0.90f);
        await svc.ProcessAsync(session, clean, uow, CancellationToken.None);

        turn.InitialQuestionText.Should().Contain("SSD lifespan");
        recorder.DidNotReceive().Record(Arg.Is<BoundaryDecisionRecord>(r => r.Route == "AsrDropped"));
    }

    // ── BoundarySplitGuard integration tests ────────────────────────────────

    private static IQuestionBoundaryClassifier NewQuestionClassifier(double confidence, string normalized)
    {
        var c = Substitute.For<IQuestionBoundaryClassifier>();
        c.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.NewQuestion, confidence,
                ShouldGenerateAnswer: true, ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true, NormalizedQuestionText: normalized, Reason: "test")));
        return c;
    }

    [Fact]
    public async Task Guard_RecentLiveTurn_LowConfidenceNewQuestion_AppendsInsteadOfSplitting()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered, live

        // Classifier always returns NewQuestion at 0.80. First item is an AdditionalRequirement
        // (Rule 8 fires on "Also …" with PreliminaryReady, confidence 0.85 ≥ 0.7 — AI not called),
        // so it routes without going through the guard.
        // Second item is a declarative that the heuristic can't classify confidently (Rule 12,
        // confidence 0.30) → AI called → NewQuestion 0.80. Guard: within window, 0.80 < 0.90 → append.
        var classifier = NewQuestionClassifier(0.80, "the cache invalidation strategy is tricky");
        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var first = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(2));
        var second = MakeItem(Speaker.Other, "the cache invalidation strategy is tricky", T0.AddSeconds(2));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "a recent, low-confidence NewQuestion must append, not split");
        turn.InitialQuestionText.Should().Contain("the cache invalidation strategy is tricky");
    }

    [Fact]
    public async Task Guard_RecentLiveTurn_HighConfidenceNewQuestion_StillSplits()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        // First item: AdditionalRequirement (Rule 8, high-confidence, no guard). Stamps the turn at T0.
        // Second item: declarative → AI called → NewQuestion 0.95. Guard: 2s within window,
        // 0.95 ≥ 0.90 → Split.
        var classifier = NewQuestionClassifier(0.95, "the kubernetes networking layer details");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var first = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(2));
        var second = MakeItem(Speaker.Other, "the kubernetes networking layer details", T0.AddSeconds(2));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "high-confidence NewQuestion clears the split bar even when recent");
    }

    [Fact]
    public async Task Guard_PastRecencyWindow_LowConfidenceNewQuestion_Splits()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        // First item: AdditionalRequirement (Rule 8, high-confidence). Stamps the turn at T0.
        // Second item: declarative → AI called → NewQuestion 0.80. Guard: 7s > 6s window → Split.
        var classifier = NewQuestionClassifier(0.80, "the kubernetes networking layer details");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var first = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(7));
        var second = MakeItem(Speaker.Other, "the kubernetes networking layer details", T0.AddSeconds(7));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "a NewQuestion past the recency window splits even at low confidence");
    }

    [Fact]
    public async Task Guard_RecordsDecision_ForEveryOtherItem()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var recorder = Substitute.For<IBoundaryDecisionRecorder>();

        // Use a declarative text so the heuristic is low-confidence (Rule 12 → NoQuestion 0.30),
        // forcing the AI classifier to run and return NewQuestion 0.95. FinalLabel is then NewQuestion.
        var classifier = NewQuestionClassifier(0.95, "the dependency injection container matters here");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time, recorder);

        var item = MakeItem(Speaker.Other, "the dependency injection container matters here", T0);
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        recorder.Received(1).Record(Arg.Is<BoundaryDecisionRecord>(r =>
            r.FinalLabel == BoundaryLabel.NewQuestion && r.SessionId == session.Id.Value));
    }

    [Fact]
    public async Task Guard_HeuristicSourcedNewQuestion_RecentLiveTurn_StillSplits()
    {
        // Regression: the guard must protect against AI mislabels only. The heuristic emits
        // NewQuestion (0.85, below the 0.90 split bar) solely on explicit new-topic markers
        // ("Now …?"), so it is a reliable split signal and must NOT be demoted to an append even
        // when a turn was active moments ago. (Previously this merged two distinct questions.)
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("What is dependency injection?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is dependency injection?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered, live, recent

        // Classifier must NOT be consulted — the heuristic is confident (0.85 ≥ 0.7).
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "Now explain CQRS in one sentence?", T0.AddSeconds(1));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2,
            "a heuristic NewQuestion on an explicit new-topic marker splits even within the recency window");
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
    }
}
