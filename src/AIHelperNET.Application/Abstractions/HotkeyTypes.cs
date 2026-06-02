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
    ToggleOverlay = 5
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

/// <summary>Win32 virtual key codes used for hotkey registration.</summary>
public enum VirtualKey : uint
{
    /// <summary>Space bar.</summary>
    Space = 0x20,
    /// <summary>S key.</summary>
    S = 0x53,
    /// <summary>Q key.</summary>
    Q = 0x51,
    /// <summary>C key.</summary>
    C = 0x43,
    /// <summary>H key.</summary>
    H = 0x48
}
