namespace AIHelperNET.Application.Abstractions;

/// <summary>A default global-hotkey binding: the key combination and the action it triggers.</summary>
/// <param name="Id">The logical hotkey this binding is for.</param>
/// <param name="Modifiers">The modifier keys held down for the combination.</param>
/// <param name="Key">The virtual key pressed.</param>
/// <param name="Description">A short, human-readable description of the action, for display.</param>
public sealed record HotkeyBinding(
    HotkeyId Id, ModifierKeys Modifiers, VirtualKey Key, string Description)
{
    /// <summary>
    /// The human-readable key combination in conventional order, e.g. <c>"Ctrl+Shift+Space"</c>.
    /// </summary>
    public string Gesture
    {
        get
        {
            var parts = new List<string>(5);
            if (Modifiers.HasFlag(ModifierKeys.Ctrl)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(ModifierKeys.Win)) parts.Add("Win");
            parts.Add(HotkeyKeys.Display(Key));
            return string.Join("+", parts);
        }
    }
}

/// <summary>A user override of one action's default global-hotkey chord. Persisted in settings;
/// merged against <see cref="HotkeyDefaults.All"/> by <see cref="HotkeyDefaults.Resolve"/>.</summary>
/// <param name="Id">The action whose chord is overridden.</param>
/// <param name="Modifiers">The replacement modifier keys.</param>
/// <param name="Key">The replacement virtual key.</param>
public sealed record HotkeyOverride(HotkeyId Id, ModifierKeys Modifiers, VirtualKey Key);

/// <summary>
/// The application's default global-hotkey bindings — the single source of truth shared by hotkey
/// registration (<c>App.WireHotkeys</c>) and the read-only shortcut list in Settings. Keep this in
/// sync with the registration site by registering from <see cref="All"/> rather than hard-coding.
/// </summary>
public static class HotkeyDefaults
{
    /// <summary>All default bindings, one per <see cref="HotkeyId"/>.</summary>
    public static IReadOnlyList<HotkeyBinding> All { get; } =
    [
        new(HotkeyId.ToggleSession,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Space, "Start / stop session"),
        new(HotkeyId.CaptureScreen,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.S,     "Capture screen"),
        new(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q,     "Generate answer"),
        new(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.C,     "Copy answer"),
        new(HotkeyId.ToggleOverlay,          ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.H,     "Show / hide overlay"),
        new(HotkeyId.AnswerLatestQuestion,   ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Z,     "Answer latest question"),
    ];

    /// <summary>The effective bindings = <see cref="All"/> with any matching <see cref="HotkeyOverride.Id"/>
    /// replaced by the override's chord. Descriptions always come from the defaults.</summary>
    /// <param name="overrides">User chord overrides; empty or null ⇒ pure defaults.</param>
    /// <returns>The merged effective bindings.</returns>
    public static IReadOnlyList<HotkeyBinding> Resolve(IReadOnlyList<HotkeyOverride> overrides)
    {
        if (overrides is null || overrides.Count == 0) return All;
        var map = overrides.GroupBy(o => o.Id).ToDictionary(g => g.Key, g => g.First());
        return All.Select(b => map.TryGetValue(b.Id, out var o)
            ? b with { Modifiers = o.Modifiers, Key = o.Key }
            : b).ToList();
    }
}
