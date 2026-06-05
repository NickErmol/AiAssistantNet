using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace AIHelperNET.Infrastructure.Ocr;

public static class ScreenGrabber
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax,
        IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc,
        uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    private const uint WINEVENT_OUTOFCONTEXT   = 0x0000;

    private static IntPtr          _hookHandle       = IntPtr.Zero;
    private static IntPtr          _lastContentWindow = IntPtr.Zero;
    private static WinEventDelegate? _procDelegate;   // kept alive to prevent GC

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>Starts tracking foreground window changes so <see cref="CaptureForeground"/> can
    /// find the last user-facing content window even when AIHelper has focus.</summary>
    public static void StartTracking()
    {
        _procDelegate = OnForegroundChanged;
        _hookHandle   = SetWinEventHook(
            EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _procDelegate, 0, 0, WINEVENT_OUTOFCONTEXT);
    }

    /// <summary>Stops the foreground-tracking hook.</summary>
    public static void StopTracking()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWinEvent(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private static void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (hwnd == IntPtr.Zero) return;
        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid != (uint)Environment.ProcessId)
            _lastContentWindow = hwnd;
    }

    public static Bitmap CapturePrimary()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
            ?? new Rectangle(0, 0, 1920, 1080);

        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bmp;
    }

    public static Bitmap CaptureForeground()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
            return CapturePrimary();

        _ = GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Environment.ProcessId)
        {
            // AIHelper itself is focused (user clicked Capture button or used hotkey while AIHelper
            // was active). Use the last non-AIHelper window recorded by the WinEvent hook.
            hwnd = _lastContentWindow;
            if (hwnd == IntPtr.Zero)
                return CapturePrimary();
        }

        if (!GetClientRect(hwnd, out RECT r))
            return CapturePrimary();

        var clientSize = new Size(r.Right, r.Bottom);
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
            return CapturePrimary();

        // logical coords — may drift on non-100% DPI monitors
        var pt = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hwnd, ref pt))
            return CapturePrimary();
        var origin = new Point(pt.X, pt.Y);

        var bmp = new Bitmap(clientSize.Width, clientSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(origin, Point.Empty, clientSize);
        return bmp;
    }
}
