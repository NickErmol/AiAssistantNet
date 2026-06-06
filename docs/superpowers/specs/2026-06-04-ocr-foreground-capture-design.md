# OCR Foreground Window Capture

**Date:** 2026-06-04  
**Branch:** `feature/ocr-foreground-capture`  
**Status:** Approved

## Problem

`Ctrl+Shift+S` currently calls `ScreenGrabber.CapturePrimary()`, which reads the entire primary screen. This produces noisy OCR output because it includes the AIHelperNET overlay, Claude Code, dev tools, and any other open windows alongside the target content (browser with a coding challenge, Word, Teams).

## Goal

Capture only the client area of the foreground content window — the window the user is actively looking at when they press the hotkey.

## Chosen Approach

Self-contained in `ScreenGrabber` + `WindowsOcrService`. No changes to Application layer types (`IScreenOcrService`, `CaptureScreenCommand`, or any ViewModel).

## Affected Files

| File | Change |
|------|--------|
| `src/AIHelperNET.Infrastructure/Ocr/ScreenGrabber.cs` | Add `CaptureForeground()` static method |
| `src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs` | Call `CaptureForeground()` instead of `CapturePrimary()` |

All other files are unchanged.

## ScreenGrabber.CaptureForeground() — Design

### Win32 calls required

```
GetForegroundWindow()         → IntPtr hwnd
GetWindowThreadProcessId()    → uint processId  (compare with current process)
GetClientRect(hwnd)           → RECT (width × height, always origin-relative)
ClientToScreen(hwnd, ref pt)  → translates top-left (0,0) to screen coords
```

### Logic

```
hwnd = GetForegroundWindow()
if hwnd == IntPtr.Zero → CapturePrimary()

GetWindowThreadProcessId(hwnd, out uint pid)
if pid == (uint)Process.GetCurrentProcess().Id → CapturePrimary()

GetClientRect(hwnd, out RECT r)
clientSize = new Size(r.Right, r.Bottom)
if clientSize.Width <= 0 || clientSize.Height <= 0 → CapturePrimary()   // minimized

pt = new POINT(0, 0)
ClientToScreen(hwnd, ref pt)
origin = new Point(pt.X, pt.Y)

bmp = new Bitmap(clientSize.Width, clientSize.Height, Format32bppArgb)
g.CopyFromScreen(origin, Point.Empty, clientSize)
return bmp
```

### Why `WDA_EXCLUDEFROMCAPTURE` handles the overlay

The overlay has stealth mode on by default (`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)`). `CopyFromScreen` uses `BitBlt` internally — Windows composits the desktop without excluded windows before passing pixels to the API. The overlay will not appear in any capture, even when it visually overlaps the foreground window.

### Fallback conditions (all delegate to CapturePrimary)

| Condition | Reason |
|-----------|--------|
| `GetForegroundWindow()` returns zero | No foreground window (lock screen, UAC prompt) |
| Process ID matches current process | Foreground window is the overlay itself |
| Client rect is zero-sized | Window is minimized |

## Multi-monitor

`ClientToScreen` returns absolute screen coordinates. `CopyFromScreen` accepts negative X/Y for monitors left-of-primary. No special handling needed.

## Testing

`ScreenGrabber` and `WindowsOcrService` have no unit tests (pure Win32/GDI infrastructure). Verification is manual:

1. Run app → stealth on by default
2. Focus a browser window displaying a coding challenge
3. Press `Ctrl+Shift+S`
4. Confirm the generated answer reflects only the browser content, not surrounding windows

## Non-goals

- Scrolling multi-page capture
- User selection of capture region
- Window title bar / chrome inclusion (client area only, by design)
