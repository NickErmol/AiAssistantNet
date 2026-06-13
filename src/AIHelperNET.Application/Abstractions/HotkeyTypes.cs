namespace AIHelperNET.Application.Abstractions;

/// <summary>Identifies a registered global hotkey.</summary>
public enum HotkeyId
{
    /// <summary>Start or stop a session.</summary>
    ToggleSession = 1,
    /// <summary>Capture the current screen.</summary>
    CaptureScreen = 2,
    /// <summary>Generate an answer for the current question.</summary>
    GenerateAnswer = 3,
    /// <summary>Copy the current answer to clipboard.</summary>
    CopyAnswer = 4,
    /// <summary>Show or hide the overlay window.</summary>
    ToggleOverlay = 5,
    /// <summary>Derive and answer the latest question from recent transcript + captures.</summary>
    AnswerLatestQuestion = 6
}

/// <summary>Win32 modifier key flags for hotkey registration.</summary>
public enum ModifierKeys : uint
{
    /// <summary>No modifier.</summary>
    None = 0,
    /// <summary>Alt key.</summary>
    Alt = 1,
    /// <summary>Ctrl key.</summary>
    Ctrl = 2,
    /// <summary>Shift key.</summary>
    Shift = 4,
    /// <summary>Windows key.</summary>
    Win = 8
}

/// <summary>Win32 virtual key codes that can be bound to a global hotkey.</summary>
public enum VirtualKey : uint
{
    /// <summary>Space bar.</summary>
    Space = 0x20,
    /// <summary>0 key.</summary>
    D0 = 0x30,
    /// <summary>1 key.</summary>
    D1 = 0x31,
    /// <summary>2 key.</summary>
    D2 = 0x32,
    /// <summary>3 key.</summary>
    D3 = 0x33,
    /// <summary>4 key.</summary>
    D4 = 0x34,
    /// <summary>5 key.</summary>
    D5 = 0x35,
    /// <summary>6 key.</summary>
    D6 = 0x36,
    /// <summary>7 key.</summary>
    D7 = 0x37,
    /// <summary>8 key.</summary>
    D8 = 0x38,
    /// <summary>9 key.</summary>
    D9 = 0x39,
    /// <summary>A key.</summary>
    A = 0x41,
    /// <summary>B key.</summary>
    B = 0x42,
    /// <summary>C key.</summary>
    C = 0x43,
    /// <summary>D key.</summary>
    D = 0x44,
    /// <summary>E key.</summary>
    E = 0x45,
    /// <summary>F key.</summary>
    F = 0x46,
    /// <summary>G key.</summary>
    G = 0x47,
    /// <summary>H key.</summary>
    H = 0x48,
    /// <summary>I key.</summary>
    I = 0x49,
    /// <summary>J key.</summary>
    J = 0x4A,
    /// <summary>K key.</summary>
    K = 0x4B,
    /// <summary>L key.</summary>
    L = 0x4C,
    /// <summary>M key.</summary>
    M = 0x4D,
    /// <summary>N key.</summary>
    N = 0x4E,
    /// <summary>O key.</summary>
    O = 0x4F,
    /// <summary>P key.</summary>
    P = 0x50,
    /// <summary>Q key.</summary>
    Q = 0x51,
    /// <summary>R key.</summary>
    R = 0x52,
    /// <summary>S key.</summary>
    S = 0x53,
    /// <summary>T key.</summary>
    T = 0x54,
    /// <summary>U key.</summary>
    U = 0x55,
    /// <summary>V key.</summary>
    V = 0x56,
    /// <summary>W key.</summary>
    W = 0x57,
    /// <summary>X key.</summary>
    X = 0x58,
    /// <summary>Y key.</summary>
    Y = 0x59,
    /// <summary>Z key.</summary>
    Z = 0x5A,
    /// <summary>F1 key.</summary>
    F1 = 0x70,
    /// <summary>F2 key.</summary>
    F2 = 0x71,
    /// <summary>F3 key.</summary>
    F3 = 0x72,
    /// <summary>F4 key.</summary>
    F4 = 0x73,
    /// <summary>F5 key.</summary>
    F5 = 0x74,
    /// <summary>F6 key.</summary>
    F6 = 0x75,
    /// <summary>F7 key.</summary>
    F7 = 0x76,
    /// <summary>F8 key.</summary>
    F8 = 0x77,
    /// <summary>F9 key.</summary>
    F9 = 0x78,
    /// <summary>F10 key.</summary>
    F10 = 0x79,
    /// <summary>F11 key.</summary>
    F11 = 0x7A,
    /// <summary>F12 key.</summary>
    F12 = 0x7B
}

/// <summary>A bindable key paired with its display label, for the Settings key dropdown.</summary>
/// <param name="Key">The virtual key.</param>
/// <param name="Display">The label shown to the user (e.g. <c>"5"</c> for <see cref="VirtualKey.D5"/>).</param>
public sealed record KeyChoice(VirtualKey Key, string Display);

/// <summary>Display and selection helpers for <see cref="VirtualKey"/>.</summary>
public static class HotkeyKeys
{
    /// <summary>Friendly label for a key — digits show as a bare number (<c>"5"</c>), everything else
    /// uses the enum name (<c>"A"</c>, <c>"F1"</c>, <c>"Space"</c>).</summary>
    /// <param name="key">The key to label.</param>
    /// <returns>The display label.</returns>
    public static string Display(VirtualKey key)
    {
        // Digit keys (VK 0x30–0x39) display as the bare character ("5"); everything else uses the enum name.
        return key is >= (VirtualKey)0x30 and <= (VirtualKey)0x39
            ? ((char)(uint)key).ToString()
            : key.ToString();
    }

    /// <summary>All bindable keys, ordered for the dropdown: A–Z, 0–9, F1–F12, Space.</summary>
    public static IReadOnlyList<KeyChoice> Selectable { get; } = BuildSelectable();

    private static List<KeyChoice> BuildSelectable()
    {
        var letters = Enumerable.Range('A', 26).Select(c => (VirtualKey)c);
        var digits  = Enumerable.Range(0, 10).Select(d => (VirtualKey)(0x30 + d));
        var fkeys   = Enumerable.Range(0, 12).Select(f => (VirtualKey)(0x70 + f));
        var all     = letters.Concat(digits).Concat(fkeys).Append(VirtualKey.Space);
        return all.Select(k => new KeyChoice(k, Display(k))).ToList();
    }
}
