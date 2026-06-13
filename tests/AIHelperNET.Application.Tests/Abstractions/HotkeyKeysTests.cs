using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Abstractions;

public class HotkeyKeysTests
{
    [Theory]
    [InlineData(VirtualKey.A, "A")]
    [InlineData(VirtualKey.Z, "Z")]
    [InlineData(VirtualKey.D0, "0")]
    [InlineData(VirtualKey.D9, "9")]
    [InlineData(VirtualKey.F1, "F1")]
    [InlineData(VirtualKey.F12, "F12")]
    [InlineData(VirtualKey.Space, "Space")]
    public void Display_ReturnsFriendlyName(VirtualKey key, string expected)
        => HotkeyKeys.Display(key).Should().Be(expected);

    [Fact]
    public void Selectable_CoversLettersDigitsFKeysAndSpace_WithNoDuplicates()
    {
        var keys = HotkeyKeys.Selectable.Select(c => c.Key).ToList();
        keys.Should().Contain([VirtualKey.A, VirtualKey.Z, VirtualKey.D0, VirtualKey.D9,
            VirtualKey.F1, VirtualKey.F12, VirtualKey.Space]);
        keys.Should().OnlyHaveUniqueItems();
        keys.Count.Should().Be(26 + 10 + 12 + 1); // A-Z, 0-9, F1-F12, Space
    }

    [Fact]
    public void Digit_GestureUsesBareNumber()
    {
        var b = new HotkeyBinding(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Alt,
            VirtualKey.D5, "test");
        b.Gesture.Should().Be("Ctrl+Alt+5");
    }
}
