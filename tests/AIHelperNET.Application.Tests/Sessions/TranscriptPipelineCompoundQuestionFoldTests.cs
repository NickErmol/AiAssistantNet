using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
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

/// <summary>
/// Compound questions arrive as rapid QuestionComplete pairs — "…what are the major technical
/// challenges you had" then 2 s later "How did you overcome that?". In the 2026-07-06 session 5 of
/// 6 detected questions split into two cards this way. A QuestionComplete within the fold window of
/// the active turn's last activity, with NO candidate speech in between, folds into that turn
/// (append + debounced regen) instead of opening a second card. Candidate speech in between means
/// the previous question was answered — quick-fire rounds still get separate cards.
/// </summary>
public class TranscriptPipelineCompoundQuestionFoldTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static (TranscriptPipelineService svc, Session session, IUnitOfWork uow, FakeTimeProvider time,
        IMediator mediator) Make()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

        var boundary = Substitute.For<IQuestionBoundaryClassifier>();
        boundary.ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionComplete, 0.95, true, false, true, "x", "test")));

        var mediator = Substitute.For<IMediator>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var time = new FakeTimeProvider(T0);
        var svc = new TranscriptPipelineService(
            factory, Substitute.For<ITranscriptSink>(), Substitute.For<IConversationTurnSink>(),
            Substitute.For<IQuestionClassifier>(),
            boundaryClassifier: boundary, timeProvider: time);
        return (svc, session, uow, time, mediator);
    }

    private static TranscriptItem Other(string text, DateTimeOffset ts)
        => TranscriptItem.Create(Speaker.Other, text, ts, 0.9f);

    [Fact]
    public async Task RapidSecondQuestion_FoldsIntoTheActiveTurn()
    {
        var (svc, session, uow, time, _) = Make();

        await svc.ProcessAsync(session,
            Other("Could you talk through what are the major technical challenges you had?", T0), uow, default);
        session.ConversationTurns.Should().HaveCount(1);

        time.Advance(TimeSpan.FromSeconds(2));
        await svc.ProcessAsync(session,
            Other("How did you overcome that?", T0.AddSeconds(2)), uow, default);

        session.ConversationTurns.Should().HaveCount(1,
            "a follow-up fragment 2 s later is the same compound question, not a second card");
        session.ConversationTurns[0].InitialQuestionText.Should().Contain("overcome");
    }

    [Fact]
    public async Task SecondQuestionAfterCandidateSpoke_OpensANewTurn()
    {
        var (svc, session, uow, time, _) = Make();

        await svc.ProcessAsync(session, Other("What is a primary key?", T0), uow, default);

        time.Advance(TimeSpan.FromSeconds(2));
        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Me, "A unique row identifier.", T0.AddSeconds(2), 0.9f), uow, default);

        time.Advance(TimeSpan.FromSeconds(2));
        await svc.ProcessAsync(session, Other("And what is an index?", T0.AddSeconds(4)), uow, default);

        session.ConversationTurns.Should().HaveCount(2,
            "the candidate answered in between — this is a quick-fire round, not a compound question");
    }

    [Fact]
    public async Task SecondQuestionBeyondTheFoldWindow_OpensANewTurn()
    {
        var (svc, session, uow, time, _) = Make();

        await svc.ProcessAsync(session, Other("What is a primary key?", T0), uow, default);

        time.Advance(TimeSpan.FromSeconds(10));
        await svc.ProcessAsync(session, Other("What is dependency injection?", T0.AddSeconds(10)), uow, default);

        session.ConversationTurns.Should().HaveCount(2, "10 s is well past the fold window");
    }

    [Fact]
    public async Task FoldWindowBoundary_ExactlySixSecondsFolds_JustOverSplits()
    {
        // Pins the <= comparison: elapsed == window folds; one millisecond more splits.
        var (svc, session, uow, time, _) = Make();
        await svc.ProcessAsync(session, Other("What is a primary key?", T0), uow, default);
        time.Advance(TimeSpan.FromSeconds(6));
        await svc.ProcessAsync(session, Other("And how do you enforce it?", T0.AddSeconds(6)), uow, default);
        session.ConversationTurns.Should().HaveCount(1, "exactly the window edge still folds");

        var (svc2, session2, uow2, time2, _) = Make();
        await svc2.ProcessAsync(session2, Other("What is a primary key?", T0), uow2, default);
        time2.Advance(TimeSpan.FromSeconds(6) + TimeSpan.FromMilliseconds(1));
        await svc2.ProcessAsync(session2, Other("What is dependency injection?", T0.AddSeconds(7)), uow2, default);
        session2.ConversationTurns.Should().HaveCount(2, "past the window edge splits");
    }

    [Fact]
    public async Task Fold_FiresADebouncedRegenerationForTheExistingTurn()
    {
        var (svc, session, uow, time, mediator) = Make();

        await svc.ProcessAsync(session,
            Other("Could you talk through the major technical challenges you had?", T0), uow, default);
        time.Advance(TimeSpan.FromSeconds(2));
        await svc.ProcessAsync(session, Other("How did you overcome that?", T0.AddSeconds(2)), uow, default);

        time.Advance(TimeSpan.FromSeconds(2)); // past the 1 s regen debounce
        await Task.Delay(200);                 // let the fire-and-forget Task.Run dispatch

        await mediator.Received().Send(
            Arg.Is<AIHelperNET.Application.Answers.Commands.GenerateAnswerCommand>(
                c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }
}
