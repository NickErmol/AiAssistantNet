using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TurnStatusFeedbackTests
{
    [Fact]
    public void TryDrain_WhenEmpty_ReturnsFalse()
    {
        var sut = new TurnStatusFeedback();
        sut.TryDrain(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_ThenDrain_ReturnsSameEvent()
    {
        var sut = new TurnStatusFeedback();
        var turnId = ConversationTurnId.New();
        sut.Publish(new TurnStatusEvent(turnId, ConversationTurnStatus.PreliminaryReady));

        sut.TryDrain(out var e).Should().BeTrue();
        e.TurnId.Should().Be(turnId);
        e.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        sut.TryDrain(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_PreservesFifoOrder()
    {
        var sut = new TurnStatusFeedback();
        var t1 = ConversationTurnId.New();
        var t2 = ConversationTurnId.New();
        sut.Publish(new TurnStatusEvent(t1, ConversationTurnStatus.GeneratingPreliminary));
        sut.Publish(new TurnStatusEvent(t2, ConversationTurnStatus.GeneratingPreliminary));
        sut.Publish(new TurnStatusEvent(t1, ConversationTurnStatus.PreliminaryReady));

        sut.TryDrain(out var a).Should().BeTrue();
        sut.TryDrain(out var b).Should().BeTrue();
        sut.TryDrain(out var c).Should().BeTrue();
        a.TurnId.Should().Be(t1);
        a.Status.Should().Be(ConversationTurnStatus.GeneratingPreliminary);
        b.TurnId.Should().Be(t2);
        c.TurnId.Should().Be(t1);
        c.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
    }
}
