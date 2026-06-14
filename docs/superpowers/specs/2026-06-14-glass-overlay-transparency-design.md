# Glass overlay transparency — design

**Date:** 2026-06-14
**Status:** Approved design, pending spec review
**Scope:** `AIHelperNET.App` (WPF overlay) only. No Domain/Application/Infrastructure changes.

## Goal

Make the `MainOverlayWindow` render as a single translucent "glass" sheet over whatever
is on screen, while **all text, icons, and the status dot stay fully opaque and crisp**.
A slider in Settings → Appearance controls how see-through the overlay is, live.

This replaces the current `Window.Opacity = 0.75` behavior, which fades *everything
including text* and hurts readability.

User decisions captured during brainstorming:
- **Scope = Option A (whole overlay glass):** title bar, sidebar, panels, and answer
  cards all become translucent (one glass sheet), not just the answer area.
- **Slider = repurpose the existing one:** reuse the existing `overlayOpacity` setting +
  slider + `OpacityChanged` event; relabel "Opacity" → "Transparency". No new settings
  field, no migration. The old text-dimming behavior is dropped.
- **Default strength:** keep current `0.75`. Slider range stays `0.2`–`1.0`.

## HARD REQUIREMENT — stealth has priority

Stealth capture-exclusion (`WDA_EXCLUDEFROMCAPTURE`, `SetWindowDisplayAffinity` in
`MainOverlayWindow.xaml.cs`) is **non-negotiable and takes priority over this feature.**

- The feature ships **only if** the overlay still fully disappears from screen capture
  (screen-share / recording) with `AllowsTransparency="True"`.
- This must be **verified manually** (screen-share/record the desktop, confirm the glass
  overlay is invisible in the capture, toggle 🎥 on/off) before merge — it is not
  log-verifiable.
- **If `AllowsTransparency` breaks stealth in any way, stealth wins:** abandon or
  fall back (e.g. keep the text-crisp improvement without `AllowsTransparency`, or shelve
  the feature). Do not ship reduced stealth to gain transparency.

## Mechanism

WPF can only show the desktop *through* a window when `AllowsTransparency="True"`
(requires `WindowStyle="None"`, already set). The current `Opacity="0.75"` is a separate,
inferior knob that fades rendered content (text too). The plan:

1. **`MainOverlayWindow.xaml`**
   - Add `AllowsTransparency="True"`. Set `Opacity="1.0"` (remove the 0.75 fade) so text
     is always crisp.
   - Window `Background` becomes a **translucent** brush whose alpha = the transparency
     setting. Because nested panels currently paint opaque backgrounds on top, the inner
     surface brushes used by the overlay (`Brush.Background.Panel / Sidebar / Card /
     TitleBar / Window`) must also become translucent so the *whole* overlay reads as
     glass (Option A). Keep `Brush.Foreground.*`, accent, and semantic brushes fully
     opaque so text/icons stay crisp.

2. **Live alpha control**
   - A value converter turns the `double` opacity (0.2–1.0) into a `SolidColorBrush` /
     `Color` with that alpha applied to each glass surface color.
   - On `OpacityChanged`, the overlay rebuilds/updates the glass brushes (or sets the
     alpha on dedicated overlay-local brush instances) so the slider updates live without
     restart, matching today's live behavior.
   - Implementation choice (to settle in the plan): drive a small set of **overlay-local**
     translucent brushes (not the shared theme resources used by `SettingsWindow`) so the
     Settings window is unaffected. This avoids transparency bleeding into other windows.

3. **Settings UI (`SettingsWindow.xaml`)**
   - Relabel the Appearance → "Opacity" field to **"Transparency"** (and the helper text /
     percentage readout accordingly). Same `OverlayOpacity` binding, same range.

4. **Persistence**
   - Unchanged. `overlayOpacity` already round-trips through `AppSettingsDto` and
     `settings.json`. No EF migration. Existing saved value carries over and now means
     glass strength.

## Components touched

| File | Change |
|------|--------|
| `Windows/MainOverlayWindow.xaml` | `AllowsTransparency="True"`, `Opacity="1.0"`, translucent glass backgrounds |
| `Windows/MainOverlayWindow.xaml.cs` | apply/update glass alpha on `OpacityChanged`; ensure order with `OnSourceInitialized`/`ApplyStealth` |
| `Windows/SettingsWindow.xaml` | relabel "Opacity" → "Transparency" |
| (maybe) a new `Converters/OpacityToBrushConverter.cs` or overlay-local brush helper | opacity → ARGB brush |

No changes to `SettingsViewModel` plumbing (`OverlayOpacity`, `OpacityChanged`) — reused.

## Data flow

```
Settings "Transparency" slider
  → SettingsViewModel.OverlayOpacity (existing)
  → OnOverlayOpacityChanged → OpacityChanged event (existing)
  → MainOverlayWindow handler → update overlay-local glass brushes' alpha (live)
  → persisted as overlayOpacity in settings.json on Save (existing)
```

## Testing & verification

- **Manual, gating — stealth:** screen-share/record desktop; confirm overlay is invisible
  in capture with glass on; toggle 🎥; confirm. (Priority requirement above.)
- **Manual — visual:** drag Transparency slider 0.2↔1.0; confirm background gets
  see-through while text/icons stay crisp; confirm live update, no restart.
- **Manual — window behaviors:** resize-with-grip and title-bar drag still work with
  `AllowsTransparency` (layered window).
- **Automated:** existing `App.Tests` / FlaUI UI tests must still pass. Note FlaUI
  caveat — `AllowsTransparency` windows are still standard HWNDs; `GetAllTopLevelWindows`
  discovery unaffected. Add an assertion that the Appearance label reads "Transparency"
  if cheap; otherwise rely on manual.
- **Build:** `dotnet build` clean (TreatWarningsAsErrors). Stop the running overlay first
  (locks output DLLs).

## Out of scope (YAGNI)

- Real blur / acrylic / Mica frosted-glass (needs DWM interop; revisit later if wanted).
- Per-panel independent transparency controls.
- Any second/extra opacity slider.
- Theme (light/dark) changes beyond making overlay surface brushes carry alpha.

## Risks

- **Stealth interaction with `AllowsTransparency`** — primary risk; gated above.
- **Layered-window perf** — negligible for a small overlay; software composition only for
  this window.
- **Brush sharing bleed** — mitigated by using overlay-local brushes, not shared theme
  resources, for the translucent surfaces.
