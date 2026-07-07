using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>
/// Verifies that every answer-generation handler reads <see cref="AppSettingsDto.CandidateProfileCard"/>
/// from the settings store and passes it to the prompt builder so it appears in the captured prompt.
/// </summary>
public class HandlerCandidateProfileTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private const string CardSubstring = "CANDIDATE_PROFILE_SENTINEL_XYZ";

    private static async IAsyncEnumerable<string> Stream(params string[] chunks)
    {
        foreach (var c in chunks) { yield return c; await Task.Yield(); }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static (ISettingsStore settingsStore, IAnswerProvider provider, IAnswerProviderResolver resolver)
        MakeProviderWithCapture(out AnswerPrompt?[] captured)
    {
        var capturedArr = new AnswerPrompt?[1];
        captured = capturedArr;

        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedArr[0] = ci.ArgAt<AnswerPrompt>(0);
                return Stream("ok");
            });
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settingsStore = Substitute.For<ISettingsStore>();
        return (settingsStore, provider, resolver);
    }

    private static AppSettingsDto SettingsWithCard(string card) =>
        new AppSettingsDto(AiBackend.Claude, WhisperModelSize.Base,
            AnswerSettings.Default, CodeProfile.Empty,
            MicDeviceId: null, LoopbackDeviceId: null)
        { CandidateProfileCard = card };

    private static AppSettingsDto SettingsWithoutCard() =>
        new AppSettingsDto(AiBackend.Claude, WhisperModelSize.Base,
            AnswerSettings.Default, CodeProfile.Empty,
            MicDeviceId: null, LoopbackDeviceId: null);

    // ── GenerateAnswerHandler ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAnswerHandler_WhenSettingsHaveCard_PromptSystemContainsFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is DI?", T0).Value;

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithCard(CardSubstring)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateAnswerHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            new TurnStatusFeedback(), NullLogger<GenerateAnswerHandler>.Instance);

        await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        captured[0]!.System.Should().Contain(CardSubstring);
    }

    [Fact]
    public async Task GenerateAnswerHandler_WhenSettingsHaveNoCard_PromptSystemDoesNotContainFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is DI?", T0).Value;

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithoutCard()));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateAnswerHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            new TurnStatusFeedback(), NullLogger<GenerateAnswerHandler>.Instance);

        await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().NotContain(CardSubstring);
        captured[0]!.System.Should().NotContain("--- BEGIN UNTRUSTED DATA ---");
    }

    // ── GenerateFollowUpHandler ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateFollowUpHandler_WhenSettingsHaveCard_PromptSystemContainsFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is CQRS?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(FluentResults.Result.Ok(session));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithCard(CardSubstring)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateFollowUpHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            NullLogger<GenerateFollowUpHandler>.Instance);

        await handler.Handle(
            new GenerateFollowUpCommand(session.Id, turn.Id, "Can you expand?"),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        captured[0]!.System.Should().Contain(CardSubstring);
    }

    [Fact]
    public async Task GenerateFollowUpHandler_WhenSettingsHaveNoCard_PromptSystemDoesNotContainFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What is CQRS?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(FluentResults.Result.Ok(session));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithoutCard()));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateFollowUpHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            NullLogger<GenerateFollowUpHandler>.Instance);

        await handler.Handle(
            new GenerateFollowUpCommand(session.Id, turn.Id, "Can you expand?"),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().NotContain(CardSubstring);
        captured[0]!.System.Should().NotContain("--- BEGIN UNTRUSTED DATA ---");
    }

    // ── RegenerateAnswerWithScreenHandler ──────────────────────────────────────────────────────

    [Fact]
    public async Task RegenerateAnswerWithScreenHandler_WhenSettingsHaveCard_PromptSystemContainsFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Debug this", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Debug this", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithCard(CardSubstring)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new RegenerateAnswerWithScreenHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            NullLogger<RegenerateAnswerWithScreenHandler>.Instance);

        await handler.Handle(
            new RegenerateAnswerWithScreenCommand(
                session.Id, turn.Id, "some OCR text",
                ScreenAnalysisMode.SolveCodingTask, []),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        captured[0]!.System.Should().Contain(CardSubstring);
    }

    [Fact]
    public async Task RegenerateAnswerWithScreenHandler_WhenSettingsHaveNoCard_PromptSystemDoesNotContainFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Debug this", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Debug this", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithoutCard()));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new RegenerateAnswerWithScreenHandler(
            repo, resolver, settingsStore, streamSink, uow, TimeProvider.System,
            NullLogger<RegenerateAnswerWithScreenHandler>.Instance);

        await handler.Handle(
            new RegenerateAnswerWithScreenCommand(
                session.Id, turn.Id, "some OCR text",
                ScreenAnalysisMode.SolveCodingTask, []),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().NotContain(CardSubstring);
        captured[0]!.System.Should().NotContain("--- BEGIN UNTRUSTED DATA ---");
    }

    // ── GenerateScreenFollowUpHandler ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateScreenFollowUpHandler_WhenSettingsHaveCard_PromptSystemContainsFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement LRU", T0).Value;
        cardA.TransitionTo(ConversationTurnStatus.GeneratingRefined);
        cardA.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, "class Lru {}", T0));
        cardA.TransitionTo(ConversationTurnStatus.RefinedReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithCard(CardSubstring)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateScreenFollowUpHandler(
            repo, resolver, settingsStore, streamSink, turnSink, uow,
            new ScreenTaskContextStore(), TimeProvider.System,
            NullLogger<GenerateScreenFollowUpHandler>.Instance);

        await handler.Handle(
            new GenerateScreenFollowUpCommand(
                session.Id, cardA.Id, "Implement LRU", "Implement LRU",
                ScreenAnalysisMode.SolveCodingTask, ["make it thread-safe"], []),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        captured[0]!.System.Should().Contain(CardSubstring);
    }

    [Fact]
    public async Task GenerateScreenFollowUpHandler_WhenSettingsHaveNoCard_PromptSystemDoesNotContainFence()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement LRU", T0).Value;
        cardA.TransitionTo(ConversationTurnStatus.GeneratingRefined);
        cardA.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, "class Lru {}", T0));
        cardA.TransitionTo(ConversationTurnStatus.RefinedReady);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var (settingsStore, _, resolver) = MakeProviderWithCapture(out var captured);
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SettingsWithoutCard()));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateScreenFollowUpHandler(
            repo, resolver, settingsStore, streamSink, turnSink, uow,
            new ScreenTaskContextStore(), TimeProvider.System,
            NullLogger<GenerateScreenFollowUpHandler>.Instance);

        await handler.Handle(
            new GenerateScreenFollowUpCommand(
                session.Id, cardA.Id, "Implement LRU", "Implement LRU",
                ScreenAnalysisMode.SolveCodingTask, ["make it thread-safe"], []),
            CancellationToken.None);

        captured[0].Should().NotBeNull();
        captured[0]!.System.Should().NotContain(CardSubstring);
        captured[0]!.System.Should().NotContain("--- BEGIN UNTRUSTED DATA ---");
    }
}
