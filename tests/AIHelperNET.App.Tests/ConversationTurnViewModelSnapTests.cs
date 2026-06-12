using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

public class ConversationTurnViewModelSnapTests
{
    private static ConversationTurnViewModel NewVm() =>
        new(Substitute.For<IMediator>(), TimeProvider.System, new ScreenTaskContextStore());

    [Fact]
    public void NewVersion_SnapsDisplayedToNewest_EvenWhenPagedBack()
    {
        var vm = NewVm();
        var turnId = ConversationTurnId.New();
        vm.AddTurn(turnId, "q");
        var turn = vm.GetTurn(turnId)!;

        vm.OnChunk(turnId, AnswerVersionType.Preliminary, "first"); // v1, displayed = v1
        vm.OnError(turnId, "boom");                                 // v2 (newest), should snap forward
        turn.DisplayedVersion.Should().BeSameAs(turn.AnswerVersions[0]);
        turn.VersionPositionLabel.Should().Be("v2 / 2");

        turn.ShowOlder();                                           // page back to v1
        turn.VersionPositionLabel.Should().Be("v1 / 2");

        vm.OnError(turnId, "boom2");                                // v3 (newest), snap forward again
        turn.DisplayedVersion.Should().BeSameAs(turn.AnswerVersions[0]);
        turn.VersionPositionLabel.Should().Be("v3 / 3");
    }
}
