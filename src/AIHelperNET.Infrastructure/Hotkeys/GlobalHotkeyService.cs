using System.Runtime.InteropServices;
using System.Windows.Interop;
using AIHelperNET.Application.Abstractions;
using FluentResults;

namespace AIHelperNET.Infrastructure.Hotkeys;

public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _source;
    private readonly List<HotkeyId> _registered = [];

    public event EventHandler<HotkeyId>? HotkeyPressed;

    public void Initialize(IntPtr parentHwnd)
    {
        _source = HwndSource.FromHwnd(parentHwnd);
        _source?.AddHook(WndProc);
    }

    public Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key)
    {
        if (_source is null)
            return Result.Fail("HotkeyService not initialized — call Initialize(hwnd) first.");

        var ok = RegisterHotKey(_source.Handle, (int)id, (uint)modifiers, (uint)key);
        if (ok) _registered.Add(id);
        return ok
            ? Result.Ok()
            : Result.Fail($"Failed to register hotkey {id} (already in use by another app?).");
    }

    public void UnregisterAll()
    {
        if (_source is null) return;
        foreach (var id in _registered)
            UnregisterHotKey(_source.Handle, (int)id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(this, (HotkeyId)wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
