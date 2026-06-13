using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public class HotkeyRowViewModelTests
{
    private static HotkeyRowViewModel Row() =>
        HotkeyRowViewModel.FromBinding(HotkeyDefaults.All.Single(b => b.Id == HotkeyId.GenerateAnswer));

    [Fact]
    public void FromBinding_PopulatesFlagsAndKey()
    {
        var row = Row(); // default Ctrl+Shift+Q
        row.Id.Should().Be(HotkeyId.GenerateAnswer);
        row.Ctrl.Should().BeTrue();
        row.Shift.Should().BeTrue();
        row.Alt.Should().BeFalse();
        row.Win.Should().BeFalse();
        row.SelectedKey.Should().Be(VirtualKey.Q);
        row.Gesture.Should().Be("Ctrl+Shift+Q");
    }

    [Fact]
    public void ChangingFlags_UpdatesGesture_AndModifiers()
    {
        var row = Row();
        row.Shift = false;
        row.Alt = true;

        row.ToModifiers().Should().Be(ModifierKeys.Ctrl | ModifierKeys.Alt);
        row.Gesture.Should().Be("Ctrl+Alt+Q");
    }

    [Fact]
    public void SetChord_ReplacesEverything_AndClearsError()
    {
        var row = Row();
        row.ErrorMessage = "boom";

        row.SetChord(ModifierKeys.Win, VirtualKey.D5);

        row.Ctrl.Should().BeFalse();
        row.Win.Should().BeTrue();
        row.SelectedKey.Should().Be(VirtualKey.D5);
        row.Gesture.Should().Be("Win+5");
        row.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public void Gesture_RaisesPropertyChanged_WhenChordChanges()
    {
        var row = Row();
        using var monitor = row.Monitor();

        row.SelectedKey = VirtualKey.J;

        monitor.Should().RaisePropertyChangeFor(r => r.Gesture);
    }
}
