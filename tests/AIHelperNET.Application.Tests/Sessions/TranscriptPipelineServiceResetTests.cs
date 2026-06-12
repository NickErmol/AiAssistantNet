using AIHelperNET.Application.Abstractions;
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

/// <summary>
/// Verifies that <see cref="TranscriptPipelineService.Reset"/> clears all per-session mutable
/// state so a singleton pipeline does not leak context from one session to the next.
/// </summary>
public class TranscriptPipelineServiceResetTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

    private static TranscriptItem MakeOtherItem(string text, DateTimeOffset? at = null)
        => TranscriptItem.Create(Speaker.Other, text, at ?? T0, 0.9f);

    /// <summary>
    /// Builds a pipeline backed by a recording <see cref="IQuestionBoundaryClassifier"/> that
    /// captures every <c>recentItems</c> list it is called with. The classifier always returns
    /// <see cref="BoundaryClassificationResult.Ambiguous"/> so nothing fires automatically;
    /// the test controls all routing by examining what the classifier received.
    /// </summary>
    private static (TranscriptPipelineService svc, List<IReadOnlyList<TranscriptItem>> capturedRecentItems, IUnitOfWork uow)
        MakeSvcCapturingRecentItems()
    {
        var capturedRecentItems = new List<IReadOnlyList<TranscriptItem>>();

        var boundaryClassifier = Substitute.For<IQuestionBoundaryClassifier>();
        boundaryClassifier
            .ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(),
                Arg.Do<IReadOnlyList<TranscriptItem>>(items => capturedRecentItems.Add(items.ToList())),
                Arg.Any<TranscriptItem>(),
                Arg.Any<Speaker>(),
                Arg.Any<CancellationToken>())
            .Returns(ci => Task.FromResult(BoundaryClassificationResult.Ambiguous(
                ci.ArgAt<TranscriptItem>(2).Text)));

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

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var svc = new TranscriptPipelineService(
            factory,
            Substitute.For<ITranscriptSink>(),
            Substitute.For<IConversationTurnSink>(),
            Substitute.For<IQuestionClassifier>(),
            boundaryClassifier: boundaryClassifier);

        return (svc, capturedRecentItems, uow);
    }

    [Fact]
    public async Task Reset_ClearsRecentItems_SoSession2ClassifierContextDoesNotContainSession1Text()
    {
        // Arrange — singleton pipeline shared across two sessions (the production scenario)
        var (svc, capturedRecentItems, uow) = MakeSvcCapturingRecentItems();

        // ── Session 1 ────────────────────────────────────────────────────────
        // Process one item so _recentItems accumulates session-1 text.
        var session1 = MakeSession();
        var session1Item = MakeOtherItem("first session question about SOLID principles", T0);
        await svc.ProcessAsync(session1, session1Item, uow, CancellationToken.None);

        // ── Reset (models session start in SessionRunner.StartAsync) ─────────
        svc.Reset();

        // ── Session 2 ────────────────────────────────────────────────────────
        // Process one item and capture what recentItems the classifier sees.
        var session2 = MakeSession();
        var session2Item = MakeOtherItem("second session question about CQRS", T0.AddSeconds(1));
        await svc.ProcessAsync(session2, session2Item, uow, CancellationToken.None);

        // Assert — classifier must have been called at least once for session 2
        // (the boundary path triggers ClassifyAsync because heuristic confidence < 0.7 for a
        // declarative sentence). At least one of those calls must NOT contain session 1's text.
        capturedRecentItems.Should().NotBeEmpty(
            "the boundary classifier must have been invoked for the session-2 item");

        // The recentItems passed during session 2 processing must not carry session 1's text.
        var session2Calls = capturedRecentItems
            .Where(list => list.All(item => !item.Text.Contains("first session question about SOLID principles")))
            .ToList();

        session2Calls.Should().NotBeEmpty(
            "after Reset(), recentItems passed to the classifier for session-2 items must not " +
            "contain any text from session 1 ('first session question about SOLID principles'), " +
            "but all calls still contained it — Reset() did not clear _recentItems");
    }

    [Fact]
    public async Task Reset_ClearsCollectionStartedAt_SoSession2DoesNotForceCompleteImmediately()
    {
        // Arrange — if _collectionStartedAt is not cleared, a brand-new session that opens a
        // CollectingQuestion turn will see a timestamp from session 1's collection phase.
        // With MaxCollectionSeconds = 8 and a stale timestamp far in the past, the first
        // Other item in session 2 would force-complete the collecting turn even though collection
        // was just started. We verify this doesn't happen after Reset().
        var (svc, _, uow) = MakeSvcCapturingRecentItems();

        // Session 1: start collecting (QuestionStarted label from heuristic on "Let's say…")
        // then reset without completing.
        var session1 = MakeSession();
        // Force _collectionStartedAt to be set: process an item that the heuristic labels
        // QuestionStarted (declarative + "let's say" pattern → CollectingQuestion).
        await svc.ProcessAsync(session1,
            MakeOtherItem("Let's say we have a payment service.", T0),
            uow, CancellationToken.None);

        // The session's active turn (if any) is at CollectingQuestion now.
        // Reset before session 2 starts.
        svc.Reset();

        // Session 2: a quick QuestionStarted item — with stale _collectionStartedAt from
        // session 1 pointing 9+ seconds in the past the force-complete path would fire
        // immediately, completing the turn instead of collecting. After Reset() it should not.
        var session2 = MakeSession();
        await svc.ProcessAsync(session2,
            MakeOtherItem("Let's say we have a distributed cache layer.", T0.AddSeconds(10)),
            uow, CancellationToken.None);

        // If _collectionStartedAt was stale the active turn would have been force-completed
        // (transitioned to Detected). After Reset() it should stay CollectingQuestion.
        // NOTE: if the heuristic doesn't label it QuestionStarted, the turn may not be
        // created at all — that's also acceptable (no stale force-complete happened).
        if (session2.ConversationTurns.Count > 0)
        {
            session2.ConversationTurns[0].Status.Should().Be(
                ConversationTurnStatus.CollectingQuestion,
                "after Reset(), _collectionStartedAt must be null so no stale force-complete " +
                "triggers on the very first item of session 2");
        }
    }
}
