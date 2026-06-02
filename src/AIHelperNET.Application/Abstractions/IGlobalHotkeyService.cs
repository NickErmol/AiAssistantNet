using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for registering and listening to global hotkeys.</summary>
public interface IGlobalHotkeyService
{
    /// <summary>Registers a global hotkey combination.</summary>
    /// <param name="id">Logical hotkey identifier.</param>
    /// <param name="modifiers">Modifier keys.</param>
    /// <param name="key">Virtual key code.</param>
    Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key);

    /// <summary>Unregisters all previously registered hotkeys.</summary>
    void UnregisterAll();

    /// <summary>Raised when a registered hotkey is pressed.</summary>
    event EventHandler<HotkeyId> HotkeyPressed;
}
