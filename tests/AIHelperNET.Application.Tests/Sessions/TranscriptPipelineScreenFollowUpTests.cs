using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
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

public class TranscriptPipelineScreenFollowUpTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static BoundaryClassificationResult Label(BoundaryLabel l) =>
        new(l, 0.95, false, false, false, "x", "test");

    // Session pre-seeded with a capture card; store registered for that card.
    private static (TranscriptPipelineService svc, Session session, ScreenTaskContextStore store, IUnitOfWork uow,
        IQuestionBoundaryClassifier boundary)
        Make(BoundaryLabel classifierReturns)
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement LRU cache", T0).Value;

        var store = new ScreenTaskContextStore();
        store.Register(cardA.Id, "Implement LRU cache", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        var boundary = Substitute.For<IQuestionBoundaryClassifier>();
        boundary.ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Label(classifierReturns)));

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
            boundaryClassifier: boundary, screenStore: store);
        return (svc, session, store, uow, boundary);
    }

    [Fact]
    public async Task InterviewerAddition_WhileScreenTaskActive_AccumulatesAddition()
    {
        var (svc, session, store, uow, _) = Make(BoundaryLabel.AdditionalRequirement);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "now make it thread-safe", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().ContainSingle().Which.Should().Be("now make it thread-safe");
    }

    [Fact]
    public async Task InterviewerNewQuestion_WhileScreenTaskActive_ClearsLinkage()
    {
        var (svc, session, store, uow, _) = Make(BoundaryLabel.NewQuestion);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "next question, explain hash maps", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().BeNull("an interviewer new question drops the screen-task linkage");
    }

    [Fact]
    public async Task InterviewerNoise_WhileScreenTaskActive_LeavesLinkageUntouched()
    {
        var (svc, session, store, uow, _) = Make(BoundaryLabel.NoQuestion);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "hmm, okay", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().BeEmpty();
    }

    // Regression guard for the live-audio failure: the boundary classifier must be given the captured
    // on-screen task as context, otherwise (as observed in production) it sees a context-free imperative,
    // labels it NoQuestion, and the follow-up is silently dropped. We assert the classifier *receives*
    // the task — the one thing the earlier stub-the-label tests never checked.
    [Fact]
    public async Task ScreenFollowUpClassification_GivesClassifierTheCapturedTaskAsContext()
    {
        var (svc, session, store, uow, boundary) = Make(BoundaryLabel.NoQuestion);

        IReadOnlyList<TranscriptItem>? seen = null;
        boundary.ClassifyAsync(default!, default!, default!, default, default)
            .ReturnsForAnyArgs(ci =>
            {
                seen = ci.ArgAt<IReadOnlyList<TranscriptItem>>(1);
                return Task.FromResult(Label(BoundaryLabel.NoQuestion));
            });

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "now make it thread-safe", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        seen.Should().NotBeNull("the screen-task branch must classify the interviewer utterance");
        seen!.Should().Contain(i => i.Text.Contains("LRU cache"),
            "the classifier must see the captured on-screen task so it can recognise an addition to it");
    }
}
