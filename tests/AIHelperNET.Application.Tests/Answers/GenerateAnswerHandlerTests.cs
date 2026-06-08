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
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>Unit tests for <see cref="GenerateAnswerHandler"/>.</summary>
public class GenerateAnswerHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static async IAsyncEnumerable<string> Stream(params string[] chunks)
    {
        foreach (var c in chunks) { yield return c; await Task.Yield(); }
    }

    private static (GenerateAnswerHandler handler, TurnStatusFeedback feedback,
                    ISessionRepository repo, Session session, ConversationTurn turn)
        Make()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "Explain DI.", T0, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stream("Dependency ", "injection."));
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettingsDto(
                AiBackend.Claude,
                WhisperModelSize.Base,
                AnswerSettings.Default,
                CodeProfile.Empty,
                MicDeviceId: null,
                LoopbackDeviceId: null)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var feedback = new TurnStatusFeedback();

        var handler = new GenerateAnswerHandler(
            repo, resolver, settings, streamSink, uow, TimeProvider.System, feedback);

        return (handler, feedback, repo, session, turn);
    }

    /// <summary>
    /// Verifies that the handler publishes GeneratingPreliminary then PreliminaryReady events
    /// via <see cref="ITurnStatusFeedback"/>, and does NOT call <see cref="ISessionRepository.Update"/>.
    /// </summary>
    [Fact]
    public async Task Handle_PublishesGeneratingThenReady_AndDoesNotCallRepositoryUpdate()
    {
        var (handler, feedback, repo, session, turn) = Make();

        var result = await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var published = new List<TurnStatusEvent>();
        while (feedback.TryDrain(out var e)) published.Add(e);

        published.Select(e => e.Status).Should().ContainInOrder(
            ConversationTurnStatus.GeneratingPreliminary,
            ConversationTurnStatus.PreliminaryReady);
        published.Should().OnlyContain(e => e.TurnId == turn.Id);

        repo.DidNotReceive().Update(Arg.Any<Session>());
    }

    /// <summary>
    /// Verifies that clarification transcript items attached to a turn AFTER it was created are
    /// included in the prompt context when the answer is (re)generated.
    /// </summary>
    [Fact]
    public async Task Handle_IncludesClarificationTranscriptItemsInPrompt()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "Explain DI.", T0, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;

        // A candidate clarification recorded AFTER the turn was created.
        var clarification = TranscriptItem.Create(
            Speaker.Me, "Do you mean constructor injection specifically?", T0.AddSeconds(5), 0.9f);
        session.AddTranscriptItem(clarification);
        turn.AttachClarificationQuestion(clarification.Id);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        AnswerPrompt? captured = null;
        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.ArgAt<AnswerPrompt>(0); return Stream("ok"); });
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettingsDto(
                AiBackend.Claude,
                WhisperModelSize.Base,
                AnswerSettings.Default,
                CodeProfile.Empty,
                MicDeviceId: null,
                LoopbackDeviceId: null)));
        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateAnswerHandler(
            repo, resolver, settings, streamSink, uow, TimeProvider.System, new TurnStatusFeedback());

        await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.RefinedAfterClarification),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.User.Should().Contain("constructor injection specifically");
    }
}
