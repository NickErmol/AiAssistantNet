using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class GenerateFollowUpHandlerTests
{
    /// <summary>
    /// Verifies that when <c>session.StartAnswer</c> fails (here because the session is
    /// stopped before the handler runs), the handler returns a failure result AND the
    /// turn's status is NOT changed to <see cref="ConversationTurnStatus.GeneratingRefined"/>.
    /// </summary>
    [Fact]
    public async Task Handle_StartAnswerFails_ReturnsFailureWithoutTransitioningTurn()
    {
        // Arrange
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var now = clock.GetUtcNow();

        // Build a real session so we can exercise the domain logic end-to-end.
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, now).Value;

        var question = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, now);
        session.AddDetectedQuestion(question);

        // Add a conversation turn while the session is still active.
        var turn = session.AddConversationTurn(question.Id, question.Text, now).Value;

        // Advance turn to PreliminaryReady (a plausible pre-follow-up state).
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var statusBeforeHandler = turn.Status;

        // Stop the session so StartAnswer will fail ("Session is not active.").
        session.Stop(now);

        // Wire up mocks — the repository returns our pre-built session.
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(FluentResults.Result.Ok(session));

        var settingsStore = Substitute.For<ISettingsStore>();
        settingsStore.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(new AppSettingsDto(
                AiBackend.Claude,
                WhisperModelSize.Base,
                AnswerSettings.Default,
                CodeProfile.Empty,
                MicDeviceId: null,
                LoopbackDeviceId: null));

        var providerResolver = Substitute.For<IAnswerProviderResolver>();
        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();

        // The provider should never be called when StartAnswer fails.
        var provider = Substitute.For<IAnswerProvider>();
        providerResolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var handler = new GenerateFollowUpHandler(
            repo, providerResolver, settingsStore, streamSink, uow, clock,
            NullLogger<GenerateFollowUpHandler>.Instance);

        var cmd = new GenerateFollowUpCommand(session.Id, turn.Id, "Can you elaborate?");

        // Act
        var result = await handler.Handle(cmd, CancellationToken.None);

        // Assert
        result.IsFailed.Should().BeTrue("StartAnswer should fail on a stopped session");
        turn.Status.Should().Be(statusBeforeHandler,
            "the turn must not transition to GeneratingRefined when StartAnswer fails");
    }
}
