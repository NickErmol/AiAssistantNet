# In-App Shortcut Rebinding — Design

**Date:** 2026-06-14
**Status:** Approved (brainstorming) → ready for implementation plan
**Branch:** `feature/screen-answer-eval-enforced-floor` (current); implementation will branch off `develop`

## Problem

The app's six global hotkeys are hard-coded in `HotkeyDefaults.All` and the Settings →
Shortcuts tab is **read-only**. Users cannot change a shortcut. This matters in practice for
users mapping a Logitech mouse button (e.g. MX Vertical Back/Forward via Options+ "Keystroke
Assignment") to an app action: they may need a chord that doesn't collide with the mouse
button's normal use, or that their mouse software can emit cleanly. Letting users rebind the
app's chords makes that pairing convenient.

**Mouse buttons are explicitly out of scope.** Windows `RegisterHotKey` only delivers
keyboard chords; raw mouse buttons are not keys. The path remains: mouse button → (Logitech
Options+) → keystroke → app hotkey. This feature only makes the *app side* (the keystroke
chord) user-configurable.

## Current state (verified)

- **Actions:** 6 `HotkeyId` values (`ToggleSession`, `CaptureScreen`, `GenerateAnswer`,
  `CopyAnswer`, `ToggleOverlay`, `AnswerLatestQuestion`).
- **Bindings:** `HotkeyDefaults.All` — hard-coded `HotkeyBinding(Id, Modifiers, Key, Description)`,
  all currently `Ctrl+Shift+<key>` (Space/S/Q/C/H/Z).
- **Registration:** `App.WireHotkeys` calls `GlobalHotkeyService.Register(...)` once at startup,
  looping over `HotkeyDefaults.All`; a `HotkeyPressed` switch dispatches to ViewModels.
- **`GlobalHotkeyService`** (Infrastructure) wraps Win32 `RegisterHotKey`/`UnregisterHotKey`;
  already exposes `Register`, `UnregisterAll`. `Register` returns `Result.Fail` when the OS
  rejects the combo (already owned by Windows/another app).
- **`VirtualKey` enum:** only the 6 keys actually used; values are the real Win32 VK codes.
- **Persistence:** `AppSettingsDto` → `JsonSettingsStore` (System.Text.Json **Web** defaults —
  enums serialize as **numbers**). `LoadAsync` calls `dto.Normalized()`. **No EF / no migration.**
- **Settings UI:** `SettingsViewModel.Hotkeys => HotkeyDefaults.All` (read-only), bound by the
  Shortcuts `TabItem` `ItemsControl` in `SettingsWindow.xaml`. Save flow = `SaveSettingsAsync`
  builds an `AppSettingsDto` and sends `SaveSettingsCommand`. Precedent for App→overlay live
  updates exists via the `OpacityChanged` event.
- **Tests:** `AIHelperNET.App.Tests` runs headless against the App assembly (constructs
  `SettingsViewModel` manually). `HotkeyDefaultsTests` exists in Application.Tests.

## Decisions (from brainstorming)

1. **Binding scope:** keyboard chords only (Win32 `RegisterHotKey` path). No mouse-button hook.
2. **Capture UX:** **hybrid** — modifier checkboxes + key dropdown (explicit) **and** a
   press-the-keys recorder per row.
3. **Validation:** require a modifier; block internal duplicates; surface OS/other-app
   conflicts on `RegisterHotKey` failure.
4. **Apply timing:** apply on **Save** (consistent with the rest of Settings), with per-row
   **Reset** and a **Reset all to defaults**.

## Design

### 1. Persistence — store *overrides*, not the full set

Add a lean record and a field on `AppSettingsDto`:

```csharp
/// <summary>A user override of one action's default global-hotkey chord.</summary>
public sealed record HotkeyOverride(HotkeyId Id, ModifierKeys Modifiers, VirtualKey Key);

// AppSettingsDto:
IReadOnlyList<HotkeyOverride> HotkeyOverrides { get; init; } = [];
```

The **effective** bindings are computed, never persisted as a whole:

```csharp
// HotkeyDefaults:
public static IReadOnlyList<HotkeyBinding> Resolve(IReadOnlyList<HotkeyOverride> overrides);
```

`Resolve` returns `All` with any binding whose `Id` matches an override replaced by the
override's `Modifiers`/`Key`; `Description` always comes from the default (descriptions are not
user-editable).

**Why overrides over a full list:** forward-compatible. Adding a 7th action later means existing
users automatically get its default — no stale or missing rows. Empty/missing → pure defaults.

`AppSettingsDto.Normalized()` is extended to **drop invalid overrides** — any whose `Modifiers`
or `Key` are not defined enum values, or with a duplicate `Id` (keep first) — so a legacy or
hand-edited `settings.json` can never crash startup. Enums round-trip as numbers (existing Web
serializer behavior); no converter needed.

### 2. Expand `VirtualKey`

Expand from 6 keys to a bounded, practical set whose values remain the real Win32 VK codes:

- **Letters** A–Z, **digits** 0–9, **function keys** F1–F12, **Space**.

Each member gets a `Display` string for the dropdown (e.g. digits show `"0"` not `"D0"`).
`Enum.IsDefined((VirtualKey)vk)` then validates any captured key; anything outside the set is
rejected as an unsupported key. A `HotkeyKeys.Selectable` list (ordered for the dropdown) is
exposed for the UI.

### 3. Validation — pure & testable (Application layer)

New `HotkeyValidator` operating on a proposed effective set, returning a per-`Id` error map:

- **Require a modifier:** `Modifiers == ModifierKeys.None` → error ("Shortcuts need at least one
  modifier (Ctrl, Shift, Alt, or Win).").
- **Block internal duplicates:** two actions sharing the same `(Modifiers, Key)` → both flagged
  ("This shortcut is already used by <other action>.").

OS/other-app conflicts are **not** statically knowable; they surface only at registration (§5).
This validator is a pure function — no UI, no Win32 — and is unit-tested directly.

### 4. Re-registration bridge (App layer)

New interface so the headless `App.Tests` can stub it:

```csharp
public interface IHotkeyApplier
{
    /// <summary>Re-registers all global hotkeys to <paramref name="bindings"/>.
    /// Returns the IDs that the OS REJECTED (already in use); an empty list means full success.</summary>
    IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings);
}
```

The real implementation wraps the existing `GlobalHotkeyService`: `UnregisterAll()` → `Register`
each binding → collect the `Id`s whose `Register` returned `Result.Fail`. It does **not** own the
`HotkeyPressed` dispatch — that switch stays in `App.WireHotkeys`. Only the *registration list*
becomes dynamic. Startup calls `Apply(HotkeyDefaults.Resolve(settings.HotkeyOverrides))` instead
of the current hard-coded loop. `IHotkeyApplier` is registered in DI (singleton, App layer) and
injected into `SettingsViewModel`.

### 5. UI — editable Shortcuts tab

`SettingsViewModel.Hotkeys` (read-only) becomes:

```csharp
public ObservableCollection<HotkeyRowViewModel> HotkeyRows { get; } = [];
```

Each `HotkeyRowViewModel` (`ObservableObject`) holds:

- `HotkeyId Id`, `string Description` (read-only label).
- Editable `bool Ctrl/Shift/Alt/Win` and `VirtualKey Key` (bound to checkboxes + dropdown).
- Computed `Gesture` display string (reuses the same formatting as `HotkeyBinding.Gesture`).
- `string? ErrorMessage` (inline, shown red under the row).
- `bool IsRecording`.
- `StartRecordingCommand`, `ResetCommand` (reverts this row to its `HotkeyDefaults.All` default).

Tab-level **Reset all to defaults** button and the existing **Save** button.

**Hybrid capture:** the checkboxes + dropdown edit the chord explicitly. The per-row **Record**
button sets `IsRecording = true`; `SettingsWindow.xaml.cs` `PreviewKeyDown` (while a row is
recording) translates `KeyInterop.VirtualKeyFromKey(e.Key)` + `Keyboard.Modifiers` into our
`VirtualKey`/`ModifierKeys`, writes them onto the recording row, and exits recording. Modifier-only
key presses are ignored (keep waiting); keys outside `VirtualKey` set the row error
("Unsupported key — pick a letter, digit, F-key, or Space.") and stay in recording mode.

A static helper `KeyGestureCapture.TryTranslate(Key wpfKey, ModifierKeys winMods, out ...)` holds
the WPF→enum mapping so it can be unit-tested without a window (`KeyInterop` is static).

### 6. Apply on Save

`SaveSettingsAsync` gains hotkey handling, ordered so a bad edit never persists or kills a hotkey:

1. Build the proposed effective set from the row VMs.
2. `HotkeyValidator.Validate(...)` → if any errors, write them to the offending rows' `ErrorMessage`
   and **abort** (no persist, no re-register; existing hotkeys keep working).
3. Compute `HotkeyOverrides` (only rows differing from their default) and include them in the saved
   `AppSettingsDto`; send `SaveSettingsCommand`.
4. `IHotkeyApplier.Apply(effective)`. For any returned failed `Id`, set that row's `ErrorMessage`
   ("Already in use by Windows or another app — pick a different chord.") and **re-apply the
   last-good set** so no action is left unregistered. (Last-good = the effective set from before
   this Save; held in the VM.)

### 7. Tests

**Application.Tests**
- `HotkeyValidator`: modifier-required; internal duplicate flags both; clean set → no errors.
- `HotkeyDefaults.Resolve`: override replaces only the matching `Id`; description preserved;
  empty overrides → equals `All`.
- `AppSettingsDto`: JSON round-trip including `HotkeyOverrides`; `Normalized()` drops invalid-enum
  and duplicate-`Id` overrides; legacy file without the field → empty list.

**App.Tests** (headless; stub `IMediator` + stub `IHotkeyApplier`)
- `LoadAsync` builds one row per action from settings (override reflected; others default).
- Save with an internal duplicate → rows show errors, no `SaveSettingsCommand` sent, applier not
  called.
- Save clean → `SaveSettingsCommand` carries expected overrides; applier called once with the
  effective set.
- Applier returns a failed `Id` → that row shows the in-use error; applier called again with the
  last-good set (revert).
- `Reset` on a row restores its default; **Reset all** restores every default.
- `KeyGestureCapture.TryTranslate`: `G`+Ctrl/Shift → `(Ctrl|Shift, G)`; a modifier-only key →
  false; an unmapped key (e.g. `OemTilde`) → false.

## Scope guards

- **No EF migration** — settings are JSON only.
- **No Domain change** — `HotkeyOverride`/validator live in Application; row VMs + applier in App.
- **No new dependencies.**
- **No new privileged action on LLM output** — purely input-side; security rules unaffected.
- Mouse buttons remain via Logitech Options+ → keystroke (unchanged, out of scope).

## Files touched

**Application**
- `Abstractions/HotkeyTypes.cs` — expand `VirtualKey`; add `HotkeyKeys.Selectable`.
- `Abstractions/HotkeyDefaults.cs` — add `HotkeyOverride`, `Resolve(...)`.
- `Sessions/Dtos/AppSettingsDto.cs` — add `HotkeyOverrides`; extend `Normalized()`.
- `Abstractions/HotkeyValidator.cs` — **new**.

**App**
- `Abstractions/IHotkeyApplier.cs` + `Hotkeys/HotkeyApplier.cs` — **new** (impl wraps
  `GlobalHotkeyService`).
- `ViewModels/SettingsViewModel.cs` — `HotkeyRows`, save/validate/apply wiring, inject applier.
- `ViewModels/HotkeyRowViewModel.cs` — **new**.
- `Windows/SettingsWindow.xaml` — editable Shortcuts tab (checkboxes, key combo box, Record/Reset
  buttons, error text, Reset-all).
- `Windows/SettingsWindow.xaml.cs` — `PreviewKeyDown` recording handler.
- `Common/KeyGestureCapture.cs` — **new** static WPF→enum translator.
- `App.xaml.cs` — register `IHotkeyApplier` in DI; `WireHotkeys` registers from resolved effective
  set via the applier; dispatch switch unchanged.

**Tests**
- `AIHelperNET.Application.Tests` — validator, resolve, DTO round-trip/normalize.
- `AIHelperNET.App.Tests` — VM load/save/validate/apply/reset, translator.

## Open follow-ups (non-blocking)

- Per-application scoping is a Logitech Options+ concern, not the app's — documented for the user,
  no code.
- The `HotkeyDefaultsTests` in Application.Tests may need touch-ups if `All`'s shape is referenced;
  `All` itself is unchanged.
