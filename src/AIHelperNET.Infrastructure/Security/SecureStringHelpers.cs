using System.Runtime.InteropServices;
using System.Security;

namespace AIHelperNET.Infrastructure.Security;

/// <summary>
/// Shared helpers for converting and zeroing <see cref="SecureString"/> values.
/// Centralises the Marshal-based conversion and the unsafe zeroing loop so every
/// AI-client class uses identical handling.
/// </summary>
internal static class SecureStringHelpers
{
    /// <summary>
    /// Converts a <see cref="SecureString"/> to a managed <see cref="string"/>.
    /// The underlying BSTR is zeroed immediately after copying.
    /// </summary>
    public static string ConvertToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }

    /// <summary>
    /// Zeroes every character of <paramref name="s"/> in-place to limit the window
    /// in which a plaintext API key lives in managed memory.
    /// No-op for empty strings.
    /// </summary>
    public static unsafe void ZeroString(string s)
    {
        if (s.Length > 0)
        {
            fixed (char* p = s)
                for (int i = 0; i < s.Length; i++) p[i] = '\0';
        }
    }
}
