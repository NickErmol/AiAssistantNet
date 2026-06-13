using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Applies hotkey bindings via the live <see cref="IGlobalHotkeyService"/> (Win32). The service
/// must already be initialized with the overlay window handle before the first <see cref="Apply"/>.</summary>
/// <param name="hotkeys">The global hotkey service to register through.</param>
public sealed class HotkeyApplier(IGlobalHotkeyService hotkeys) : IHotkeyApplier
{
    /// <inheritdoc/>
    public IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        hotkeys.UnregisterAll();
        var failed = new List<HotkeyId>();
        foreach (var b in bindings)
            if (hotkeys.Register(b.Id, b.Modifiers, b.Key).IsFailed)
                failed.Add(b.Id);
        return failed;
    }
}
