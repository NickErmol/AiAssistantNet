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

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

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
            return CapturePrimary();

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
