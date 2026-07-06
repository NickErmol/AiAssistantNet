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
/// A screen-follow-up <see cref="ScreenFollowUpOutcome.Noise"/> verdict means "not about the captured
/// task" — it must NOT mean "not a question". While a screen task is in focus, Noise-classified
/// interviewer speech falls through to normal boundary routing so spoken questions still create turns.
/// Regression guard for the 2026-07-06 live session, where a garbage capture held focus and 98
/// consecutive Noise verdicts silently dropped 8 real interviewer questions over 19 minutes.
/// </summary>
public class TranscriptPipelineScreenNoiseFallThroughTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static (TranscriptPipelineService svc, Session session, ScreenTaskContextStore store, IUnitOfWork uow)
        Make(BoundaryLabel boundaryReturns)
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement an LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement an LRU cache", T0).Value;

        var store = new ScreenTaskContextStore();
        store.Register(cardA.Id, "Implement an LRU cache in C#", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        var boundary = Substitute.For<IQuestionBoundaryClassifier>();
        boundary.ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(boundaryReturns, 0.95, false, false, false, "x", "test")));

        // Production path: the dedicated screen classifier decides the follow-up outcome — here it
        // always says Noise, as it did for every real question in the live failure.
        var screenClassifier = Substitute.For<IScreenFollowUpClassifier>();
        screenClassifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ScreenFollowUpOutcome.Noise));

        var legacy = Substitute.For<IQuestionClassifier>();
        var mediator = Substitute.For<IMediator>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var svc = new TranscriptPipelineService(
            factory, Substitute.For<ITranscriptSink>(), Substitute.For<IConversationTurnSink>(), legacy,
            boundaryClassifier: boundary, screenStore: store, screenFollowUpClassifier: screenClassifier);
        return (svc, session, store, uow);
    }

    [Fact]
    public async Task NoiseVerdict_SpokenQuestionStillCreatesTurn_AndKeepsScreenFocus()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.QuestionComplete);
        var question = "So talk me through how APIM can access Key Vault.";

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, question, T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2,
            "a real spoken question must not be swallowed by a Noise screen-follow-up verdict");
        session.ConversationTurns[^1].InitialQuestionText.Should().Be(question);
        store.Current.Should().NotBeNull("Noise fall-through must not drop the screen-task focus");
    }

    [Fact]
    public async Task NoiseVerdict_ActualFillerIsStillDropped()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.Unrelated);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "Yeah, okay.", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "filler must not create turns");
        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().BeEmpty();
    }

    [Fact]
    public async Task FiveConsecutiveNoiseVerdicts_ReleaseTheScreenFocus()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.Unrelated);

        for (var i = 0; i < 4; i++)
        {
            await svc.ProcessAsync(session,
                TranscriptItem.Create(Speaker.Other, $"Yeah, okay then. ({i})", T0.AddSeconds(2 + i), 0.9f),
                uow, CancellationToken.None);
            store.Current.Should().NotBeNull($"noise #{i + 1} is below the release threshold");
        }

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "Yeah, okay then. (4)", T0.AddSeconds(6), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().BeNull(
            "a task that only ever produces Noise is stale — MOVED_ON must not be the only exit");
    }
}
