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
            parts.Add(Key.ToString());
            return string.Join("+", parts);
        }
    }
}

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
}
