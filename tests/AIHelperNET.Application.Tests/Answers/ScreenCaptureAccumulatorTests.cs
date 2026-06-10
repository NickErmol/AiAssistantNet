using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenCaptureAccumulatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(8);

    [Fact]
    public void Add_FirstCapture_IsNewGroup_RawOcr()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        var r = acc.Add("screen one", T0);
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("screen one");
    }

    [Fact]
    public void Add_SecondCaptureWithinGap_ContinuesGroup_LabeledConcat()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        var r = acc.Add("second", T0.AddSeconds(3));
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(2);
        r.CombinedOcr.Should().Be("--- Screen 1 ---\nfirst\n\n--- Screen 2 ---\nsecond");
    }

    [Fact]
    public void Add_CaptureAtGapBoundary_StartsNewGroup()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        var r = acc.Add("second", T0.AddSeconds(8)); // exactly gap → new group
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("second");
    }

    [Fact]
    public void Add_ThreeWithinGap_AccumulatesAllInOrder()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("a", T0);
        acc.Add("b", T0.AddSeconds(2));
        var r = acc.Add("c", T0.AddSeconds(4));
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(3);
        r.CombinedOcr.Should().Be("--- Screen 1 ---\na\n\n--- Screen 2 ---\nb\n\n--- Screen 3 ---\nc");
    }

    [Fact]
    public void Reset_StartsFreshGroupOnNextAdd()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        acc.Reset();
        var r = acc.Add("second", T0.AddSeconds(1)); // within gap, but reset → new group
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("second");
    }

    [Fact]
    public void Add_JustBelowGapBoundary_ContinuesGroup()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        var r = acc.Add("second", T0.Add(Gap).AddTicks(-1)); // one tick under the gap → same group
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(2);
    }

    [Fact]
    public void Add_EmptyOcrString_AcceptedAndAccumulated()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        var first = acc.Add("", T0);
        first.IsNewGroup.Should().BeTrue();
        first.Count.Should().Be(1);
        first.CombinedOcr.Should().Be("");

        var second = acc.Add("real", T0.AddSeconds(1));
        second.Count.Should().Be(2);
        second.CombinedOcr.Should().Be("--- Screen 1 ---\n\n\n--- Screen 2 ---\nreal");
    }

    [Fact]
    public void Touch_ExtendsTheGroupWindow()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);                          // group active, last activity = T0
        acc.Touch(T0.AddSeconds(20));                  // simulate a 20s generation finishing
        var r = acc.Add("second", T0.AddSeconds(23));  // 3s after the touch → same group
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(2);
    }

    [Fact]
    public void Touch_WithoutActiveGroup_NextAddStillStartsNewGroup()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Touch(T0);                                 // no group yet
        var r = acc.Add("first", T0.AddSeconds(1));
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
    }
}
