using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>Unit tests for <see cref="CreateScreenTurnHandler"/>.</summary>
public class CreateScreenTurnHandlerTests
{
    private static Session NewSession(FakeTimeProvider clock)
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, clock.GetUtcNow()).Value;

    /// <summary>
    /// Verifies that handling a valid command creates a "[Screen capture]" turn,
    /// notifies the UI sink, persists, and returns the new turn id.
    /// </summary>
    [Fact]
    public async Task Handle_CreatesScreenTurn_NotifiesUi_ReturnsTurnId()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var session = NewSession(clock);
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Result.Ok(session));
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var sink = Substitute.For<IConversationTurnSink>();

        var handler = new CreateScreenTurnHandler(repo, sink, uow, clock);
        var result = await handler.Handle(new CreateScreenTurnCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().ContainSingle().Which.Id.Should().Be(result.Value);
        sink.Received(1).OnTurnCreated(result.Value, "[Screen capture]");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a persistence failure is propagated as a failed result.
    /// </summary>
    [Fact]
    public async Task Handle_PersistenceFails_ReturnsFailure()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var session = NewSession(clock);
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Result.Ok(session));
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Fail("db error"));
        var sink = Substitute.For<IConversationTurnSink>();

        var handler = new CreateScreenTurnHandler(repo, sink, uow, clock);
        var result = await handler.Handle(new CreateScreenTurnCommand(session.Id), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
