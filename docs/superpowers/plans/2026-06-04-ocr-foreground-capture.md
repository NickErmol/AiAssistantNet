# OCR Foreground Window Capture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `Ctrl+Shift+S` capture only the client area of the foreground content window instead of the full primary screen.

**Architecture:** Add `CaptureForeground()` to the existing static `ScreenGrabber` class using four Win32 P/Invoke calls. Switch `WindowsOcrService` to call `CaptureForeground()` instead of `CapturePrimary()`. All Application-layer types (`IScreenOcrService`, `CaptureScreenCommand`, ViewModels) are untouched.

**Tech Stack:** .NET 10, C#, `System.Drawing` (GDI+), `user32.dll` P/Invoke, Windows.Media.Ocr

---

### Task 1: Add `CaptureForeground()` to `ScreenGrabber`

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Ocr/ScreenGrabber.cs`

No unit tests apply — `ScreenGrabber` is pure Win32/GDI with no DI seam. Verification is a build check.

- [ ] **Step 1: Replace the file contents of `ScreenGrabber.cs`**

The full new file (adds P/Invoke declarations, two private structs, and the `CaptureForeground` method; `CapturePrimary` is unchanged):

```csharp
using System.Diagnostics;
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

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == (uint)Process.GetCurrentProcess().Id)
            return CapturePrimary();

        if (!GetClientRect(hwnd, out RECT r))
            return CapturePrimary();

        var clientSize = new Size(r.Right, r.Bottom);
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
            return CapturePrimary();

        var pt = new POINT { X = 0, Y = 0 };
        ClientToScreen(hwnd, ref pt);
        var origin = new Point(pt.X, pt.Y);

        var bmp = new Bitmap(clientSize.Width, clientSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(origin, Point.Empty, clientSize);
        return bmp;
    }
}
```

- [ ] **Step 2: Build to verify no errors**

```
dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Infrastructure/Ocr/ScreenGrabber.cs
git commit -m "feat: add CaptureForeground() to ScreenGrabber using Win32 client-area capture"
```

---

### Task 2: Switch `WindowsOcrService` to use `CaptureForeground()`

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs` (line 20)

- [ ] **Step 1: Change the capture call**

In `WindowsOcrService.CaptureAndReadAsync`, replace:

```csharp
using var bmp = ScreenGrabber.CapturePrimary();
```

with:

```csharp
using var bmp = ScreenGrabber.CaptureForeground();
```

That is the only line that changes. No other edits to the file.

- [ ] **Step 2: Build the full solution**

```
dotnet build
```

Expected: `Build succeeded. 0 Warning(s). 0 Error(s).`

- [ ] **Step 3: Run all tests**

```
dotnet test
```

Expected: all tests pass (no OCR/screen tests exist; this confirms nothing elsewhere was broken).

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs
git commit -m "feat: capture foreground window client area for OCR instead of full primary screen"
```

---

### Task 3: Manual smoke test

> Run the app and verify the feature works end-to-end.

- [ ] **Step 1: Launch the app**

```
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj
```

Or use the existing `run-aihelper` skill if configured.

- [ ] **Step 2: Start a session**

Press `Ctrl+Shift+Space` to start a session.

- [ ] **Step 3: Set up a content window**

Open a browser and navigate to any page with visible text (e.g., a coding challenge or a Wikipedia article).

- [ ] **Step 4: Trigger OCR capture**

Click the browser window to make it foreground, then press `Ctrl+Shift+S`.

- [ ] **Step 5: Verify**

Confirm that the generated answer card in the overlay reflects only the browser's content — not dev tools, terminal output, or other surrounding windows.

- [ ] **Step 6: Test fallback — overlay as foreground**

Click on the AIHelperNET overlay to bring it to foreground, then press `Ctrl+Shift+S`. Confirm the app does not crash (it should fall back to `CapturePrimary()` silently).

- [ ] **Step 7: Merge branch**

Once verified, invoke the `superpowers:finishing-a-development-branch` skill to merge `feature/ocr-foreground-capture` into `develop`.
