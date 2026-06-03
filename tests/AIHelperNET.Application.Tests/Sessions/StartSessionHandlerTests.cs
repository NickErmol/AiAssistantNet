using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class StartSessionHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_AddsSessionAndReturnsDto()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        var handler = new StartSessionHandler(repo, uow, clock);
        var cmd = new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(SessionState.Active);
        result.Value.Mode.Should().Be(SessionMode.AudioAndScreen);
        result.Value.AudioSource.Should().Be(AudioSourceMode.Both);
        await repo.Received(1).AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithCustomMode_MapsToDto()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        var handler = new StartSessionHandler(repo, uow, clock);
        var cmd = new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty,
            SessionMode.AudioOnly, AudioSourceMode.MicrophoneOnly);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Mode.Should().Be(SessionMode.AudioOnly);
        result.Value.AudioSource.Should().Be(AudioSourceMode.MicrophoneOnly);
    }

    [Fact]
    public async Task Handle_PersistenceFails_ReturnsFailure()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Fail("db error"));
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);

        var handler = new StartSessionHandler(repo, uow, clock);
        var result = await handler.Handle(new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
