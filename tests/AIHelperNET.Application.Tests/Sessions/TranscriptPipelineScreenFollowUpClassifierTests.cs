using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Sessions;
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

/// <summary>
/// The screen-task follow-up decision must be driven by the dedicated <see cref="IScreenFollowUpClassifier"/>
/// (which sees the captured task) — NOT the audio boundary classifier, which is blind to the task and
/// labels real follow-ups <c>NoQuestion</c> (the live-audio failure that motivated this).
/// </summary>
public class TranscriptPipelineScreenFollowUpClassifierTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static BoundaryClassificationResult Boundary(BoundaryLabel l) =>
        new(l, 0.95, false, false, false, "x", "test");

    private static (TranscriptPipelineService svc, Session session, ScreenTaskContextStore store,
        IUnitOfWork uow, IScreenFollowUpClassifier screenClassifier)
        Make(ScreenFollowUpOutcome screenReturns)
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement an LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement an LRU cache", T0).Value;

        var store = new ScreenTaskContextStore();
        store.Register(cardA.Id, "Implement an LRU cache in C#", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        // Boundary classifier deliberately returns NoQuestion (the live failure) — the dedicated
        // classifier must win so this never reaches the boundary path.
        var boundary = Substitute.For<IQuestionBoundaryClassifier>();
        boundary.ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Boundary(BoundaryLabel.NoQuestion)));

        var screenClassifier = Substitute.For<IScreenFollowUpClassifier>();
        screenClassifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(screenReturns));

        var legacy = Substitute.For<IQuestionClassifier>();
        var mediator = Substitute.For<IMediator>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var transcriptSink = Substitute.For<ITranscriptSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var svc = new TranscriptPipelineService(
            factory, transcriptSink, turnSink, legacy,
            boundaryClassifier: boundary, screenStore: store, screenFollowUpClassifier: screenClassifier);
        return (svc, session, store, uow, screenClassifier);
    }

    [Fact]
    public async Task DedicatedClassifierFollowUp_AccumulatesAddition_EvenWhenBoundarySaysNoQuestion()
    {
        var (svc, session, store, uow, _) = Make(ScreenFollowUpOutcome.FollowUp);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "now make it thread-safe", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().ContainSingle().Which.Should().Be("now make it thread-safe");
    }

    [Fact]
    public async Task DedicatedClassifierMovedOn_ClearsLinkage()
    {
        var (svc, session, store, uow, _) = Make(ScreenFollowUpOutcome.MovedOn);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "tell me about a conflict with a coworker", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().BeNull("the interviewer moved to an unrelated topic");
    }

    [Fact]
    public async Task DedicatedClassifier_ReceivesCapturedTaskAndUtterance()
    {
        var (svc, session, store, uow, screenClassifier) = Make(ScreenFollowUpOutcome.FollowUp);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "now make it thread-safe", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        await screenClassifier.Received(1).ClassifyAsync(
            Arg.Is<string>(s => s.Contains("LRU cache")),
            Arg.Any<IReadOnlyList<string>>(),
            Arg.Is<string>(u => u.Contains("thread-safe")),
            Arg.Any<CancellationToken>());
    }
}
