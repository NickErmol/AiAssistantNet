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
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

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
    // Renders hardware-accelerated layers (DWM composition) into the DC.
    private const uint PW_RENDERFULLCONTENT    = 0x00000002;

    private static IntPtr           _hookHandle        = IntPtr.Zero;
    private static IntPtr           _lastContentWindow = IntPtr.Zero;
    private static WinEventDelegate? _procDelegate;     // kept alive to prevent GC

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Starts tracking foreground window changes so <see cref="CaptureForeground"/> can
    /// find the last user-facing content window even when AIHelper has focus.</summary>
    public static void StartTracking()
    {
        // Bootstrap: capture any existing foreground so CaptureForeground works on the very first
        // call before the hook has a chance to fire.
        var initial = GetForegroundWindow();
        if (initial != IntPtr.Zero)
        {
            _ = GetWindowThreadProcessId(initial, out uint initPid);
            if (initPid != (uint)Environment.ProcessId)
                _lastContentWindow = initial;
        }

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
            // AIHelper itself is focused — use the last non-AIHelper window recorded by the hook.
            hwnd = _lastContentWindow;
            if (hwnd == IntPtr.Zero)
                return CapturePrimary();
        }

        return CaptureWindow(hwnd);
    }

    /// <summary>Renders a single window into a bitmap using PrintWindow, which is DPI-correct
    /// and excludes all other windows (including the overlay) from the result.</summary>
    private static Bitmap CaptureWindow(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out RECT r))
            return CapturePrimary();

        int width  = r.Right  - r.Left;
        int height = r.Bottom - r.Top;
        if (width <= 0 || height <= 0)
            return CapturePrimary();

        var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        var hdc = g.GetHdc();
        bool ok  = PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT);
        g.ReleaseHdc(hdc);

        if (!ok)
        {
            bmp.Dispose();
            return CapturePrimary();
        }

        return bmp;
    }
}
