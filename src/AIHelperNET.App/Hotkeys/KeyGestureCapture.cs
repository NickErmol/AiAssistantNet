using System.Windows.Input;
using AIHelperNET.Application.Abstractions;
using AppModifierKeys = AIHelperNET.Application.Abstractions.ModifierKeys;
using WpfModifiers = System.Windows.Input.ModifierKeys;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Translates a WPF key press into the app's <see cref="AIHelperNET.Application.Abstractions.ModifierKeys"/>/<see cref="VirtualKey"/>.
/// Static and window-free so it can be unit-tested headless.</summary>
public static class KeyGestureCapture
{
    /// <summary>True when <paramref name="key"/> is itself a modifier (Ctrl/Shift/Alt/Win) — the recorder
    /// should keep waiting for the real key rather than treat it as the binding. (Alt arrives as
    /// <see cref="Key.System"/>.)</summary>
    /// <param name="key">The pressed key.</param>
    /// <returns><see langword="true"/> if the key is a modifier.</returns>
    public static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System;

    /// <summary>Tries to translate <paramref name="key"/> + <paramref name="wpfMods"/> into a bindable chord.
    /// Returns false for keys outside the bindable set (see <see cref="HotkeyKeys.Selectable"/>).</summary>
    /// <param name="key">The pressed key.</param>
    /// <param name="wpfMods">The WPF modifier flags held down.</param>
    /// <param name="mods">The translated app modifier flags.</param>
    /// <param name="vk">The translated virtual key (valid only when the method returns true).</param>
    /// <returns><see langword="true"/> if the key is bindable.</returns>
    public static bool TryTranslate(Key key, WpfModifiers wpfMods, out AppModifierKeys mods, out VirtualKey vk)
    {
        mods = AppModifierKeys.None;
        if (wpfMods.HasFlag(WpfModifiers.Control)) mods |= AppModifierKeys.Ctrl;
        if (wpfMods.HasFlag(WpfModifiers.Shift))   mods |= AppModifierKeys.Shift;
        if (wpfMods.HasFlag(WpfModifiers.Alt))     mods |= AppModifierKeys.Alt;
        if (wpfMods.HasFlag(WpfModifiers.Windows)) mods |= AppModifierKeys.Win;

        var code = KeyInterop.VirtualKeyFromKey(key);
        if (code != 0 && Enum.IsDefined((VirtualKey)(uint)code))
        {
            vk = (VirtualKey)(uint)code;
            return true;
        }
        vk = default;
        return false;
    }
}
