using System;
using System.Windows.Media;

namespace AIHelperNET.App.Windows;

/// <summary>
/// Pure helpers for the overlay's translucent "glass" surfaces. Keeps the alpha math
/// out of the window code-behind so it can be unit-tested.
/// </summary>
internal static class OverlayGlass
{
    /// <summary>
    /// Returns <paramref name="baseColor"/> with its alpha set from <paramref name="opacity"/>
    /// (0 = fully transparent, 1 = fully opaque). Opacity is clamped to [0, 1].
    /// </summary>
    public static Color WithAlpha(Color baseColor, double opacity)
    {
        var clamped = Math.Clamp(opacity, 0.0, 1.0);
        var alpha = (byte)Math.Round(clamped * 255.0);
        return Color.FromArgb(alpha, baseColor.R, baseColor.G, baseColor.B);
    }
}
