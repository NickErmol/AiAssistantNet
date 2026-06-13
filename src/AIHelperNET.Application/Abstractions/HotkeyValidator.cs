namespace AIHelperNET.Application.Abstractions;

/// <summary>Validates a proposed set of hotkey bindings before they are registered. Pure — no Win32,
/// no UI. OS/other-app conflicts are not knowable here; they surface only at registration time.</summary>
public static class HotkeyValidator
{
    /// <summary>Returns a map of <see cref="HotkeyId"/> → error message for every invalid binding.
    /// An empty map means the whole set is valid.</summary>
    /// <param name="bindings">The proposed effective bindings to validate.</param>
    /// <returns>Per-action error messages; empty when all bindings are valid.</returns>
    public static IReadOnlyDictionary<HotkeyId, string> Validate(IReadOnlyList<HotkeyBinding> bindings)
    {
        var errors = new Dictionary<HotkeyId, string>();

        // Rule 1: a global hotkey needs at least one modifier, or it would hijack a bare key everywhere.
        foreach (var b in bindings)
            if (b.Modifiers == ModifierKeys.None)
                errors[b.Id] = "Add a modifier (Ctrl, Shift, Alt, or Win).";

        // Rule 2: no two actions may share the same chord. (A more specific duplicate message here may
        // overwrite a Rule 1 "add a modifier" message for the same action — that is intentional.)
        foreach (var group in bindings.GroupBy(b => (b.Modifiers, b.Key)).Where(g => g.Count() > 1))
        {
            var members = group.ToList();
            foreach (var b in members)
            {
                var other = members.FirstOrDefault(m => m.Id != b.Id);
                if (other is null) continue; // duplicate record sharing the same Id — nothing to cross-reference
                errors[b.Id] = $"Same shortcut as “{other.Description}”.";
            }
        }

        return errors;
    }
}
