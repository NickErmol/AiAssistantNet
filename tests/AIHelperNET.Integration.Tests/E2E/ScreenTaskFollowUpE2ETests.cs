using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// End-to-end coverage for the screen-task interviewer follow-up feature, driven through the real
/// <see cref="TranscriptPipelineService"/>, <c>ScreenTaskContextStore</c>, and
/// <c>GenerateScreenFollowUpHandler</c> over the in-memory SQLite host. The capture is simulated the
/// way the WPF view-model does it (a screen card plus a <c>store.Register</c> call); interviewer
/// speech is fed as <see cref="Speaker.Other"/> segments with scripted boundary results.
/// </summary>
public class ScreenTaskFollowUpE2ETests : IAsyncLifetime
{
    private const string Ocr = "Implement an LRU cache with O(1) get and put";
    private const string Cond1 = "now make it thread-safe";
    private const string Cond2 = "also handle null inputs";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    private InterviewHost _host = null!;

    /// <inheritdoc/>
    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();

    /// <inheritdoc/>
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static BoundaryClassificationResult AdditionalRequirement(string text) =>
        new(BoundaryLabel.AdditionalRequirement, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: true, ShouldCreateNewTurn: false,
            NormalizedQuestionText: text, Reason: "scripted");

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    [Fact]
    public async Task InterviewerFollowUps_SpawnNewCards_Accumulate_AndNeverMutateCaptureCard()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISessionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var pipeline = sp.GetRequiredService<TranscriptPipelineService>();
        var store = sp.GetRequiredService<ScreenTaskContextStore>();

        // 1) Simulate a completed screen capture: a card with an answer, persisted, then registered
        //    with the store exactly as ConversationTurnViewModel.CaptureScreenAsync does.
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        var captureCard = SeedCaptureCard(session);
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);
        store.Register(captureCard.Id, Ocr, ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        var clock = DateTimeOffset.UnixEpoch;

        // 2) Interviewer adds the first condition → a NEW follow-up card (capture card untouched).
        //    Assertions read in-memory sinks/store, not the DB, to avoid contending with the handler's
        //    background (debounced) write on the shared in-memory SQLite cache.
        _host.Classifier.Enqueue(AdditionalRequirement(Cond1));
        await pipeline.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, Cond1, clock = clock.AddSeconds(1), 0.95f),
            uow, CancellationToken.None);

        var cardB = await WaitForNewCardAsync(1, Timeout);
        await _host.Sink.WaitForCompletionCountAsync(cardB, AnswerVersionType.ScreenFollowUp, 1, Timeout);

        cardB.Should().NotBe(captureCard.Id, "the follow-up goes to a NEW card, not the capture card");
        _host.Sink.Text(cardB, AnswerVersionType.ScreenFollowUp)
            .Should().Contain(Ocr).And.Contain(Cond1).And.Contain("class LruCache {}",
                "the captured OCR, the new condition, and the prior answer all reach the prompt");
        store.Current!.Additions.Should().ContainSingle().Which.Should().Be(Cond1);
        store.Current.LatestCardId.Should().Be(cardB, "the lineage now points at the new card");

        // 3) Interviewer layers a second condition → a THIRD card that accumulates BOTH conditions.
        _host.Classifier.Enqueue(AdditionalRequirement(Cond2));
        await pipeline.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, Cond2, clock.AddSeconds(1), 0.95f),
            uow, CancellationToken.None);

        var cardC = await WaitForNewCardAsync(2, Timeout);
        await _host.Sink.WaitForCompletionCountAsync(cardC, AnswerVersionType.ScreenFollowUp, 1, Timeout);

        cardC.Should().NotBe(cardB).And.NotBe(captureCard.Id);
        _host.Sink.Text(cardC, AnswerVersionType.ScreenFollowUp)
            .Should().Contain(Cond1).And.Contain(Cond2, "the second card accumulates ALL prior additions");
        store.Current!.Additions.Should().Equal(Cond1, Cond2);
        store.Current.LatestCardId.Should().Be(cardC);

        // The capture card was never streamed a follow-up answer (never mutated).
        _host.Sink.Text(captureCard.Id, AnswerVersionType.ScreenFollowUp).Should().BeEmpty();
        _host.Sink.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task InterviewerNewQuestion_EndsScreenTaskLinkage()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISessionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var pipeline = sp.GetRequiredService<TranscriptPipelineService>();
        var store = sp.GetRequiredService<ScreenTaskContextStore>();

        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        var captureCard = SeedCaptureCard(session);
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);
        store.Register(captureCard.Id, Ocr, ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        // A clearly new, unrelated interviewer question drops the screen-task linkage. Two scripted
        // results are queued because the MovedOn outcome falls through to the normal routing path,
        // which may consult the classifier again.
        _host.Classifier.Enqueue(NewQuestion("explain hash maps"));
        _host.Classifier.Enqueue(NewQuestion("explain hash maps"));
        await pipeline.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "next question — explain hash maps", DateTimeOffset.UnixEpoch.AddSeconds(1), 0.95f),
            uow, CancellationToken.None);

        store.Current.Should().BeNull("an interviewer new question ends the captured-task linkage");
    }

    /// <summary>Builds a completed "[Screen capture]" card with one screen answer version.</summary>
    private static ConversationTurn SeedCaptureCard(Session session)
    {
        var q = DetectedQuestion.Create("[Screen capture]", QuestionSource.Ocr, DateTimeOffset.UnixEpoch);
        session.AddDetectedQuestion(q);
        var card = session.AddConversationTurn(q.Id, "[Screen capture]", DateTimeOffset.UnixEpoch).Value;
        card.TransitionTo(ConversationTurnStatus.GeneratingRefined);
        card.AddAnswerVersion(AnswerVersion.Create(
            AnswerVersionType.UpdatedWithScreen, "class LruCache {}", DateTimeOffset.UnixEpoch));
        card.TransitionTo(ConversationTurnStatus.RefinedReady);
        return card;
    }

    /// <summary>
    /// Waits (in-memory, no DB) until the host's turn sink has recorded at least
    /// <paramref name="expectedCreatedCount"/> created cards, returning the id of the most recent one.
    /// The follow-up handler announces the card via <c>OnTurnCreated</c> before it begins streaming.
    /// </summary>
    private async Task<ConversationTurnId> WaitForNewCardAsync(int expectedCreatedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var created = _host.Cards.Created;
            if (created.Count >= expectedCreatedCount)
                return created[expectedCreatedCount - 1].Id;
            await Task.Delay(50);
        }
        throw new TimeoutException(
            $"Only {_host.Cards.Created.Count} card(s) created; expected {expectedCreatedCount} within {timeout}.");
    }
}
