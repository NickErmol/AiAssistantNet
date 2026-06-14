namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// How the overlay window renders, trading off transparency against screen-capture stealth.
/// These are mutually exclusive on Windows 10: real see-through needs a layered window, which
/// disables <c>WDA_EXCLUDEFROMCAPTURE</c>. Changing this requires an app restart.
/// </summary>
public enum OverlayMode
{
    /// <summary>
    /// Hidden from screen capture (<c>WDA_EXCLUDEFROMCAPTURE</c>). The window stays opaque; the
    /// Transparency slider only dims it via <c>Window.Opacity</c> (no true see-through). Default.
    /// </summary>
    Stealth,

    /// <summary>
    /// Real translucency: the whole overlay is see-through with crisp text, controlled by the
    /// Transparency slider. Stealth is unavailable — the overlay <b>will</b> appear in screen captures.
    /// </summary>
    SeeThrough
}
