using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenFocusReleaseValveTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly ConversationTurnId CardA = ConversationTurnId.New();
    private static readonly ConversationTurnId CardB = ConversationTurnId.New();

    [Fact]
    public void FollowUps_NeverRelease()
    {
        var valve = new ScreenFocusReleaseValve();
        for (var i = 0; i < 20; i++)
            valve.Track(CardA, ScreenFollowUpOutcome.FollowUp, T0.AddSeconds(i)).Should().BeFalse();
    }

    [Fact]
    public void ReleasesOnFifthConsecutiveNoise_NotBefore()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 5);
        for (var i = 0; i < 4; i++)
            valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(i)).Should().BeFalse($"noise #{i + 1} is below the threshold");

        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(4)).Should().BeTrue("the 5th consecutive Noise trips the valve");
    }

    [Fact]
    public void FollowUp_ResetsTheNoiseCounter()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 5);
        for (var i = 0; i < 4; i++)
            valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(i));

        valve.Track(CardA, ScreenFollowUpOutcome.FollowUp, T0.AddSeconds(4)).Should().BeFalse();

        for (var i = 0; i < 4; i++)
            valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(5 + i))
                .Should().BeFalse("a FollowUp proves the task is alive, so the count restarts");
    }

    [Fact]
    public void ReleasesWhenIdleBeyondWindow_EvenWithFewNoises()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 5, maxIdle: TimeSpan.FromMinutes(3));

        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0).Should().BeFalse("tracking starts the idle window");
        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddMinutes(3).AddSeconds(1))
            .Should().BeTrue("no FollowUp arrived within the idle window");
    }

    [Fact]
    public void NewCard_ResetsTracking()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 5);
        for (var i = 0; i < 4; i++)
            valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(i));

        valve.Track(CardB, ScreenFollowUpOutcome.Noise, T0.AddSeconds(4))
            .Should().BeFalse("a fresh capture card starts its own count");
    }

    [Fact]
    public void AfterRelease_SameCardStartsFresh()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 2);
        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0);
        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(1)).Should().BeTrue();

        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(2))
            .Should().BeFalse("release clears the state; a re-registered card is tracked anew");
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var valve = new ScreenFocusReleaseValve(maxConsecutiveNoise: 2);
        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0);
        valve.Reset();
        valve.Track(CardA, ScreenFollowUpOutcome.Noise, T0.AddSeconds(1)).Should().BeFalse();
    }
}
