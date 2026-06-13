using System.Windows.Input;
using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using Xunit;
using AppModifierKeys = AIHelperNET.Application.Abstractions.ModifierKeys;
using WpfModifiers = System.Windows.Input.ModifierKeys;

namespace AIHelperNET.App.Tests;

public class KeyGestureCaptureTests
{
    [Fact]
    public void TryTranslate_LetterWithCtrlShift_Maps()
    {
        var ok = KeyGestureCapture.TryTranslate(Key.G, WpfModifiers.Control | WpfModifiers.Shift,
            out var mods, out var key);

        ok.Should().BeTrue();
        mods.Should().Be(AppModifierKeys.Ctrl | AppModifierKeys.Shift);
        key.Should().Be(VirtualKey.G);
    }

    [Fact]
    public void TryTranslate_Digit_Maps()
    {
        var ok = KeyGestureCapture.TryTranslate(Key.D5, WpfModifiers.Control, out var mods, out var key);
        ok.Should().BeTrue();
        mods.Should().Be(AppModifierKeys.Ctrl);
        key.Should().Be(VirtualKey.D5);
    }

    [Fact]
    public void TryTranslate_UnsupportedKey_ReturnsFalse()
        => KeyGestureCapture.TryTranslate(Key.OemTilde, WpfModifiers.Control, out _, out _)
            .Should().BeFalse();

    [Theory]
    [InlineData(Key.LeftCtrl, true)]
    [InlineData(Key.RightShift, true)]
    [InlineData(Key.LWin, true)]
    [InlineData(Key.System, true)]   // Alt is delivered as Key.System in WPF
    [InlineData(Key.G, false)]
    public void IsModifierKey_DetectsModifiers(Key key, bool expected)
        => KeyGestureCapture.IsModifierKey(key).Should().Be(expected);
}
