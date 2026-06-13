using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.App.Hotkeys;

/// <summary>Re-registers the full set of global hotkeys atomically.</summary>
public interface IHotkeyApplier
{
    /// <summary>Unregisters all hotkeys, then registers <paramref name="bindings"/>. Returns the IDs the
    /// OS rejected (already in use by Windows/another app); an empty list means full success.</summary>
    /// <param name="bindings">The effective bindings to register.</param>
    /// <returns>The IDs that failed to register; empty on full success.</returns>
    IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings);
}
