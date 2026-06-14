# Glass Overlay Transparency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `MainOverlayWindow` a single see-through "glass" sheet (whole overlay) with crisp text, controlled by the existing (relabeled) Transparency slider — without weakening stealth capture-exclusion.

**Architecture:** Turn on `AllowsTransparency="True"` and drop the `Window.Opacity` fade (which dimmed text). The overlay's surface backgrounds reference new overlay-local `Glass.*` brush resources. A code-behind method rebuilds those brushes from the *current theme's* base colors at the chosen alpha, live, on opacity change and on theme toggle. Settings reuses the existing `OverlayOpacity` plumbing; only the label changes.

**Tech Stack:** WPF, C# (.NET 10), DynamicResource brushes, `SetWindowDisplayAffinity` (Win32 stealth), xUnit + FluentAssertions (App.Tests).

---

## File Structure

| File | Responsibility | Action |
|------|----------------|--------|
| `src/AIHelperNET.App/Windows/OverlayGlass.cs` | Pure helper: base color + opacity → ARGB color. Unit-testable logic. | Create |
| `tests/AIHelperNET.App.Tests/OverlayGlassTests.cs` | Tests for the alpha math. | Create |
| `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs` | Build/refresh `Glass.*` brushes; rewire `OpacityChanged`; rebuild on theme toggle. | Modify |
| `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` | `AllowsTransparency`, remove `Opacity`, point surfaces at `Glass.*`. | Modify |
| `src/AIHelperNET.App/Windows/SettingsWindow.xaml` | Relabel "Opacity" → "Transparency". | Modify |

**Glass surface mapping** (whole-overlay scope = Option A):

| Overlay surface (XAML line) | Old brush key | New `Glass.*` key |
|---|---|---|
| Window background (L10) | `Brush.Background.Window` | `Glass.Window` |
| Title bar Border (L19) | `Brush.Background.TitleBar` | `Glass.TitleBar` |
| Sidebar Border (L107) | `Brush.Background.Sidebar` | `Glass.Sidebar` |
| Transcript panel Border (L300) | `Brush.Background.Panel` | `Glass.Panel` |
| Answer panel Border (L340) | `Brush.Background.Panel` | `Glass.Panel` |
| Answer card Border (L347) | `Brush.Background.Card` | `Glass.Card` |

**Intentionally left opaque:** the two `ProgressBar` track backgrounds (L214, L221, `Brush.Background.Panel`) — tiny meter widgets, keep them legible. The `HistoryPanel` user control (separate file) stays on its theme brushes for v1 (out of scope; see spec follow-up).

---

## Task 1: Glass alpha helper (pure logic, TDD)

**Files:**
- Create: `src/AIHelperNET.App/Windows/OverlayGlass.cs`
- Test: `tests/AIHelperNET.App.Tests/OverlayGlassTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.App.Tests/OverlayGlassTests.cs`:

```csharp
using System.Windows.Media;
using AIHelperNET.App.Windows;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public sealed class OverlayGlassTests
{
    private static readonly Color Base = Color.FromRgb(0x21, 0x21, 0x3A); // Brush.Background.Window

    [Fact]
    public void WithAlpha_keeps_rgb_and_sets_alpha_from_opacity()
    {
        var c = OverlayGlass.WithAlpha(Base, 0.75);

        c.R.Should().Be(0x21);
        c.G.Should().Be(0x21);
        c.B.Should().Be(0x3A);
        c.A.Should().Be(191); // round(0.75 * 255)
    }

    [Fact]
    public void WithAlpha_opacity_one_is_fully_opaque()
        => OverlayGlass.WithAlpha(Base, 1.0).A.Should().Be(255);

    [Fact]
    public void WithAlpha_opacity_zero_is_fully_transparent()
        => OverlayGlass.WithAlpha(Base, 0.0).A.Should().Be(0);

    [Theory]
    [InlineData(-0.5, 0)]
    [InlineData(1.5, 255)]
    public void WithAlpha_clamps_out_of_range_opacity(double opacity, byte expectedAlpha)
        => OverlayGlass.WithAlpha(Base, opacity).A.Should().Be(expectedAlpha);
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~OverlayGlassTests"`
Expected: FAIL — `OverlayGlass` does not exist (compile error).

- [ ] **Step 3: Write the minimal implementation**

Create `src/AIHelperNET.App/Windows/OverlayGlass.cs`:

```csharp
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
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~OverlayGlassTests"`
Expected: PASS (4 tests / 5 cases green).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/Windows/OverlayGlass.cs tests/AIHelperNET.App.Tests/OverlayGlassTests.cs
git commit -m "feat(overlay): add OverlayGlass.WithAlpha helper for translucent surfaces"
```

---

## Task 2: Build & refresh glass brushes in code-behind

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

This task adds the brush-rebuild logic and rewires the existing opacity hookups. It does NOT yet change the XAML, so after this task the new `Glass.*` resources exist but nothing references them yet (build stays green, behavior unchanged).

- [ ] **Step 1: Add `using` for media types**

At the top of `MainOverlayWindow.xaml.cs`, add to the existing using block (after line 5 `using System.Windows.Interop;`):

```csharp
using System.Windows.Media;
```

- [ ] **Step 2: Add the glass-surface map and rebuild method**

Inside the `MainOverlayWindow` class, just below the `WDA_*` constants (after line 39 `private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;`), add:

```csharp
/// <summary>
/// Overlay surface resource keys → the theme brush each derives its color from.
/// Rebuilt as translucent <c>Glass.*</c> brushes so the whole overlay reads as glass
/// while text/icons (which use <c>Brush.Foreground.*</c>) stay fully opaque.
/// </summary>
private static readonly (string GlassKey, string ThemeKey)[] GlassSurfaces =
{
    ("Glass.Window",   "Brush.Background.Window"),
    ("Glass.TitleBar", "Brush.Background.TitleBar"),
    ("Glass.Sidebar",  "Brush.Background.Sidebar"),
    ("Glass.Panel",    "Brush.Background.Panel"),
    ("Glass.Card",     "Brush.Background.Card"),
};

/// <summary>
/// Rebuilds the overlay-local <c>Glass.*</c> brushes from the current theme's base colors
/// at the given transparency (0.2–1.0). Window-local so the Settings window's theme brushes
/// are unaffected. Safe to call repeatedly (on slider change and after a theme toggle).
/// </summary>
private void ApplyGlassOpacity(double opacity)
{
    foreach (var (glassKey, themeKey) in GlassSurfaces)
    {
        if (TryFindResource(themeKey) is not SolidColorBrush baseBrush)
            continue;

        var brush = new SolidColorBrush(OverlayGlass.WithAlpha(baseBrush.Color, opacity));
        brush.Freeze();
        Resources[glassKey] = brush;
    }
}
```

- [ ] **Step 3: Seed the glass brushes in the constructor and rewire OpacityChanged**

In the constructor, replace the existing line (currently line 59):

```csharp
        _settingsVm.OpacityChanged += opacity => Opacity = opacity;
```

with:

```csharp
        _settingsVm.OpacityChanged += ApplyGlassOpacity;
        ApplyGlassOpacity(_settingsVm.OverlayOpacity); // seed Glass.* before first render
```

- [ ] **Step 4: Use glass opacity (not Window.Opacity) when restoring persisted value**

In `OnSourceInitialized`, replace the line (currently line 73):

```csharp
            Opacity = _settingsVm.OverlayOpacity;
```

with:

```csharp
            ApplyGlassOpacity(_settingsVm.OverlayOpacity);
```

- [ ] **Step 5: Rebuild glass after a theme toggle**

Replace `ToggleTheme_Click` (currently lines 125-126):

```csharp
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        => ThemeManager.Toggle();
```

with:

```csharp
    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
    {
        ThemeManager.Toggle();
        // Theme swap replaces Brush.Background.* — rebuild Glass.* from the new colors.
        ApplyGlassOpacity(_settingsVm.OverlayOpacity);
    }
```

- [ ] **Step 6: Build to verify it compiles (overlay app must be stopped first)**

Stop any running overlay (it locks output DLLs), then:
Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs
git commit -m "feat(overlay): build live translucent Glass.* brushes from theme colors"
```

---

## Task 3: Turn on transparency and point surfaces at glass (XAML)

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

- [ ] **Step 1: Enable per-pixel transparency and remove the text-dimming opacity**

Replace lines 9-11:

```xml
        WindowStyle="None"
        Background="{DynamicResource Brush.Background.Window}"
        Opacity="0.75"
```

with:

```xml
        WindowStyle="None"
        AllowsTransparency="True"
        Background="{DynamicResource Glass.Window}"
```

(`AllowsTransparency="True"` requires `WindowStyle="None"`, already present. `Window.Opacity` is removed so it defaults to 1.0 → text stays crisp.)

- [ ] **Step 2: Point the title bar at the glass brush**

On the title-bar `Border` (line 19), replace:

```xml
                Background="{DynamicResource Brush.Background.TitleBar}"
```

with:

```xml
                Background="{DynamicResource Glass.TitleBar}"
```

- [ ] **Step 3: Point the sidebar at the glass brush**

On the sidebar `Border` (line 107), replace:

```xml
                Background="{DynamicResource Brush.Background.Sidebar}"
```

with:

```xml
                Background="{DynamicResource Glass.Sidebar}"
```

- [ ] **Step 4: Point the transcript panel at the glass brush**

On the transcript `Border` (line 300), replace:

```xml
                <Border Grid.Row="0" Background="{DynamicResource Brush.Background.Panel}"
```

with:

```xml
                <Border Grid.Row="0" Background="{DynamicResource Glass.Panel}"
```

- [ ] **Step 5: Point the answer panel at the glass brush**

On the answer `Border` (line 340), replace:

```xml
                <Border Grid.Row="2" Background="{DynamicResource Brush.Background.Panel}">
```

with:

```xml
                <Border Grid.Row="2" Background="{DynamicResource Glass.Panel}">
```

- [ ] **Step 6: Point the answer card at the glass brush**

On the answer card `Border` (line 347), replace:

```xml
                                            Background="{DynamicResource Brush.Background.Card}"
```

with:

```xml
                                            Background="{DynamicResource Glass.Card}"
```

- [ ] **Step 7: Build to verify it compiles (stop overlay first)**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 8: Commit**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): render whole overlay as see-through glass with crisp text"
```

---

## Task 4: Relabel the Settings slider

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

- [ ] **Step 1: Rename the field label**

On line 221, replace:

```xml
                    <TextBlock Text="Opacity" Style="{StaticResource FieldLabel}"/>
```

with:

```xml
                    <TextBlock Text="Transparency" Style="{StaticResource FieldLabel}"/>
```

(The slider, `OverlayOpacity` binding, range 0.2–1.0, and `{0:P0}` readout are unchanged — higher % = more opaque / less see-through.)

- [ ] **Step 2: Build to verify it compiles (stop overlay first)**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
git commit -m "feat(settings): relabel overlay Opacity slider as Transparency"
```

---

## Task 5: Full test run + manual verification (STEALTH IS GATING)

**Files:** none (verification only).

- [ ] **Step 1: Run the full App.Tests suite**

Run: `dotnet test tests/AIHelperNET.App.Tests`
Expected: all pass (including the new `OverlayGlassTests`). UI tests that discover the overlay via `GetAllTopLevelWindows` are unaffected by `AllowsTransparency` (still a top-level HWND).

- [ ] **Step 2: Launch the app**

Use the `run-aihelper` skill (stops any running instance, builds, runs). Confirm the overlay appears as translucent glass and **answer/transcript text is crisp**, not dimmed.

- [ ] **Step 3: GATING — verify stealth still works**

With the overlay showing (🎥/👁 in default stealth-on state), start a screen recording or a screen-share (e.g. Teams/Zoom share, or Win+G / OBS) and confirm **the overlay is invisible in the capture**. Toggle the 🎥/👁 button and confirm the overlay appears/disappears in the capture accordingly.

  - **If the overlay is NOT excluded from capture with `AllowsTransparency="True"`: STOP.** Stealth has priority. Revert Task 3's `AllowsTransparency="True"` (the feature cannot ship as-is) and report back — do not merge a build with weakened stealth.

- [ ] **Step 4: Verify the live Transparency slider**

Open Settings → Appearance → drag **Transparency** from 100% down toward 20%. Confirm the whole overlay (title bar, sidebar, panels, card) gets progressively see-through, text stays crisp, and it updates **live** (no restart). Save, close, reopen the app — confirm the value persists.

- [ ] **Step 5: Verify window behaviors with transparency on**

Confirm: dragging the title bar moves the window; the resize grip (bottom-right) resizes it; the theme toggle (◐) still flips light/dark and the glass rebuilds at the same transparency (no opaque flash that sticks).

- [ ] **Step 6: Note results**

Record pass/fail for steps 3–5 in the PR description. Steps 3–5 are NOT log-verifiable — they require eyeballing.

---

## Task 6: Update memory and open the PR

**Files:** memory files (outside repo) + git.

- [ ] **Step 1: Push and open the PR (only after Step 3 stealth check passes)**

```bash
git push -u origin feature/glass-overlay-transparency
gh pr create --base develop --title "feat(overlay): see-through glass transparency" --body "Whole-overlay glass via AllowsTransparency; text stays crisp. Repurposes the Opacity slider as Transparency. Stealth verified still excluded from capture. See docs/superpowers/specs/2026-06-14-glass-overlay-transparency-design.md."
```

(Repo auto-merges PRs on creation — ensure all commits are pushed first.)

- [ ] **Step 2: Update MEMORY.md**

Add a one-line pointer under Project and update CURRENT STATE: glass overlay transparency feature, branch/PR, stealth-gating result, ⏳ any manual steps still pending.

---

## Notes for the implementer

- **Stealth beats this feature, always.** If `AllowsTransparency` and capture-exclusion conflict, stealth wins (Task 5 Step 3).
- **Stop the running overlay before every build** — it locks output DLLs (MSB3027).
- **`settings.json` is untouched** — `overlayOpacity` already round-trips; no EF migration.
- **Don't touch shared theme brushes** (`Brush.Background.*`) — only the overlay references `Glass.*`; the Settings window keeps the opaque theme brushes.
- **History panel** stays opaque in v1 (separate user control) — that's an accepted scope cut, noted in the spec.
