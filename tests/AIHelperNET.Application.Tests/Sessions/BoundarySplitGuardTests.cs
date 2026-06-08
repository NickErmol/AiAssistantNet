using AIHelperNET.Application.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class BoundarySplitGuardTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(BoundarySplitGuard.RecencyWindowSeconds);
    private static readonly TimeSpan WithinWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PastWindow = TimeSpan.FromSeconds(BoundarySplitGuard.RecencyWindowSeconds + 1);

    private readonly BoundarySplitGuard _guard = new();

    [Fact]
    public void NoLiveTurn_AlwaysSplits()
    {
        _guard.Evaluate(effectiveConfidence: 0.10, hasLiveTurn: false, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_PastRecencyWindow_Splits_EvenAtLowConfidence()
    {
        _guard.Evaluate(0.10, hasLiveTurn: true, sinceLastActivity: PastWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_Recent_HighConfidence_Splits()
    {
        _guard.Evaluate(BoundarySplitGuard.SplitConfidenceBar, hasLiveTurn: true, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_Recent_LowConfidence_Appends()
    {
        _guard.Evaluate(0.80, hasLiveTurn: true, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.AppendToActiveTurn);
    }

    [Fact]
    public void LiveTurn_ExactlyAtWindowBoundary_TreatedAsRecent()
    {
        // sinceLastActivity == window is NOT past the window → recent → needs the bar
        _guard.Evaluate(0.80, hasLiveTurn: true, sinceLastActivity: Window)
            .Should().Be(SplitDecision.AppendToActiveTurn);
    }
}
