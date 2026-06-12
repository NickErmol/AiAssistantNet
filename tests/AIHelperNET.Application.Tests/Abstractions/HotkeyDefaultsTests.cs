using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyDefaultsTests
{
    [Fact]
    public void All_CoversEveryHotkeyId_Uniquely()
    {
        var ids = HotkeyDefaults.All.Select(b => b.Id).ToList();
        ids.Should().BeEquivalentTo(Enum.GetValues<HotkeyId>());
        ids.Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData(HotkeyId.ToggleSession,  "Ctrl+Shift+Space")]
    [InlineData(HotkeyId.CaptureScreen,  "Ctrl+Shift+S")]
    [InlineData(HotkeyId.GenerateAnswer, "Ctrl+Shift+Q")]
    [InlineData(HotkeyId.CopyAnswer,     "Ctrl+Shift+C")]
    [InlineData(HotkeyId.ToggleOverlay,        "Ctrl+Shift+H")]
    [InlineData(HotkeyId.AnswerLatestQuestion, "Ctrl+Shift+Z")]
    public void Gesture_FormatsModifiersThenKey(HotkeyId id, string expected)
    {
        var binding = HotkeyDefaults.All.Single(b => b.Id == id);
        binding.Gesture.Should().Be(expected);
    }

    [Fact]
    public void Gesture_OrdersModifiers_CtrlShiftAltWin()
    {
        var binding = new HotkeyBinding(
            HotkeyId.ToggleOverlay,
            ModifierKeys.Win | ModifierKeys.Alt | ModifierKeys.Shift | ModifierKeys.Ctrl,
            VirtualKey.H,
            "test");
        binding.Gesture.Should().Be("Ctrl+Shift+Alt+Win+H");
    }

    [Fact]
    public void EveryBinding_HasNonEmptyDescription()
    {
        HotkeyDefaults.All.Should().OnlyContain(b => !string.IsNullOrWhiteSpace(b.Description));
    }
}
