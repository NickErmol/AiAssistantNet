using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

/// <summary>Exercises the actual <see cref="System.Windows.Input.ICommand"/> objects the pager buttons
/// bind to (<c>ShowOlderVersionCommand</c> / <c>ShowNewerVersionCommand</c>), driven through the same
/// public version-creation paths the live app uses. This is the closest deterministic, headless proxy
/// for clicking the pager buttons.</summary>
public class VersionPagerCommandTests
{
    private static ConversationTurnViewModel NewVm() =>
        new(Substitute.For<IMediator>(), TimeProvider.System, new ScreenTaskContextStore());

    // Builds a turn carrying three versions (v1 oldest … v3 newest) via the public event paths,
    // exactly as the pipeline would: a streamed preliminary plus two later versions.
    private static (ConversationTurnViewModel vm, TurnVm turn) ThreeVersionTurn()
    {
        var vm = NewVm();
        var turnId = ConversationTurnId.New();
        vm.AddTurn(turnId, "q");
        vm.OnChunk(turnId, AnswerVersionType.Preliminary, "first");  // v1
        vm.OnError(turnId, "second");                                // v2
        vm.OnError(turnId, "third");                                 // v3 (newest, displayed)
        return (vm, vm.GetTurn(turnId)!);
    }

    [Fact]
    public void OlderCommand_PagesBack_AndDisablesAtOldest()
    {
        var (vm, turn) = ThreeVersionTurn();
        turn.VersionPositionLabel.Should().Be("v3 / 3");

        vm.ShowOlderVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v2 / 3");

        vm.ShowOlderVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v1 / 3");
        turn.CanShowOlder.Should().BeFalse("the oldest version is showing");

        // Button would be disabled here; even if invoked, it must not move past the oldest.
        vm.ShowOlderVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v1 / 3");
    }

    [Fact]
    public void NewerCommand_PagesForward_AndDisablesAtNewest()
    {
        var (vm, turn) = ThreeVersionTurn();
        vm.ShowOlderVersionCommand.Execute(turn);
        vm.ShowOlderVersionCommand.Execute(turn); // now at v1

        vm.ShowNewerVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v2 / 3");

        vm.ShowNewerVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v3 / 3");
        turn.CanShowNewer.Should().BeFalse("the newest version is showing");

        vm.ShowNewerVersionCommand.Execute(turn);
        turn.VersionPositionLabel.Should().Be("v3 / 3");
    }

    [Fact]
    public void PagerVisibilityFlag_TracksVersionCount()
    {
        var vm = NewVm();
        var turnId = ConversationTurnId.New();
        vm.AddTurn(turnId, "q");
        var turn = vm.GetTurn(turnId)!;

        turn.HasMultipleVersions.Should().BeFalse("no versions yet → no pager");

        vm.OnChunk(turnId, AnswerVersionType.Preliminary, "only");
        turn.HasMultipleVersions.Should().BeFalse("one version → still no pager");

        vm.OnError(turnId, "second");
        turn.HasMultipleVersions.Should().BeTrue("two versions → pager shows");
    }

    [Fact]
    public void Commands_AreNoOp_WhenTurnIsNull()
    {
        var vm = NewVm();
        // The XAML passes the bound TurnVm; guard against a null parameter regardless.
        vm.ShowOlderVersionCommand.Execute(null);
        vm.ShowNewerVersionCommand.Execute(null);
    }
}
