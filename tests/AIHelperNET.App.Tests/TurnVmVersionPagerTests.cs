using AIHelperNET.App.ViewModels;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public class TurnVmVersionPagerTests
{
    private static AnswerVersionVm V() =>
        new(AnswerVersionId.New(), AnswerVersionType.Preliminary, DateTimeOffset.UtcNow);

    // Builds a turn with `count` versions, newest-first (index 0 = newest), displayed = newest —
    // matching how CreateNewVersion populates the collection in production.
    private static TurnVm TurnWith(int count)
    {
        var turn = new TurnVm(ConversationTurnId.New(), "q");
        for (var i = 0; i < count; i++)
            turn.AnswerVersions.Insert(0, V());
        turn.DisplayedVersion = turn.AnswerVersions.Count > 0 ? turn.AnswerVersions[0] : null;
        return turn;
    }

    [Fact]
    public void SingleVersion_HasNoPager()
    {
        var t = TurnWith(1);
        t.HasMultipleVersions.Should().BeFalse();
        t.VersionPositionLabel.Should().Be("v1 / 1");
        t.CanShowOlder.Should().BeFalse();
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void NewestDisplayed_ShowsHighestPosition_CanGoOlderOnly()
    {
        var t = TurnWith(3);
        t.HasMultipleVersions.Should().BeTrue();
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.CanShowOlder.Should().BeTrue();
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void ShowOlder_StepsBackChronologically_AndClampsAtOldest()
    {
        var t = TurnWith(3);
        t.ShowOlder();
        t.VersionPositionLabel.Should().Be("v2 / 3");
        t.CanShowNewer.Should().BeTrue();
        t.ShowOlder();
        t.VersionPositionLabel.Should().Be("v1 / 3");
        t.CanShowOlder.Should().BeFalse();
        t.ShowOlder(); // clamp — no further movement
        t.VersionPositionLabel.Should().Be("v1 / 3");
    }

    [Fact]
    public void ShowNewer_StepsForward_AndClampsAtNewest()
    {
        var t = TurnWith(3);
        t.ShowOlder();
        t.ShowOlder(); // now at v1
        t.ShowNewer();
        t.VersionPositionLabel.Should().Be("v2 / 3");
        t.ShowNewer();
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.ShowNewer(); // clamp
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void NoVersions_PagerEmpty_AndNavigationIsSafeNoOp()
    {
        var t = new TurnVm(ConversationTurnId.New(), "q");
        t.HasMultipleVersions.Should().BeFalse();
        t.VersionPositionLabel.Should().BeEmpty();
        t.CanShowOlder.Should().BeFalse();
        t.CanShowNewer.Should().BeFalse();
        t.ShowOlder(); // must not throw
        t.ShowNewer();
    }
}
