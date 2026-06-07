# UI Automation Tests Design

**Date:** 2026-06-07  
**Scope:** Out-of-process FlaUI UI automation tests covering the E2E checklist for AIHelperNET.

---

## Goal

Convert the manual E2E checklist (`e2e-test` skill) into repeatable automated tests that can be run locally before merging any feature that touches the pipeline, audio, OCR, DI, DB schema, or UI.

---

## What is already covered

`tests/AIHelperNET.Integration.Tests/Sessions/SessionRunnerTests.cs` already has in-process tests for audio source routing:
- `BothMode_MicAndLoopbackFrames_BothEndInTranscript`
- `MicrophoneOnlyMode_LoopbackFrameDropped`
- `SystemAudioOnlyMode_MicFrameDropped`

These cover checklist sections 4a and 4b (Mic Only / System Only speaker labels) via `FakeAudioCaptureService` + `FakeTranscriptionService`. No new in-process tests are needed.

---

## Approach: FlaUI out-of-process UI automation

FlaUI (UIA3) drives the real built `.exe` out-of-process. No external services required — pure .NET, uses the Windows UI Automation API. AutomationIds already set on all radio buttons; new AutomationIds to be added to buttons and status dots.

---

## Part 1 — New test project

**Location:** `tests/AIHelperNET.UITests/`  
**Target framework:** `net10.0-windows10.0.17763.0`  
**Dependencies:** `FlaUI.UIA3`, `FlaUI.Core`, `xUnit`, `FluentAssertions`

### File structure

```
tests/AIHelperNET.UITests/
  AIHelperNET.UITests.csproj
  AppFixture.cs              — IClassFixture: kill existing instance, launch exe,
                               wait for main window, expose Application + AutomationElement
  MainWindow.cs              — typed element accessors by AutomationId
  Tests/
    StartupTests.cs          — log file assertions (VAD ready, Whisper ready)
    UIControlTests.cs        — radio buttons, theme toggle, history panel, sidebar
    SessionLifecycleTests.cs — start/stop session, dot state transitions
    ScreenCaptureTests.cs    — test image + Ctrl+Shift+S → turn card appears
    SettingsTests.cs         — settings window opens, tabs visible, device lists present
```

### AppFixture

- Kills any running `AIHelperNET.App` process on setup
- Launches `src/AIHelperNET.App/bin/Debug/net10.0-windows10.0.17763.0/AIHelperNET.App.exe`
- Polls up to 10 s for the main window to appear via `Application.GetMainWindow()`
- Exposes `Application` and `AutomationElement Window` to tests
- Tears down via `app.Kill()` in `DisposeAsync()`
- Shared across test classes via `[Collection("UITests")]` + `CollectionFixture`

### MainWindow typed wrapper

Provides named FlaUI element accessors — e.g.:

```csharp
public Button BtnToggleSession   => Window.FindFirstDescendant(cf => cf.ByAutomationId("Btn_ToggleSession")).AsButton();
public AutomationElement DotMic  => Window.FindFirstDescendant(cf => cf.ByAutomationId("StatusDot_Mic"));
```

Tests call `mainWindow.BtnToggleSession.Click()` rather than doing raw Find calls.

---

## Part 2 — XAML AutomationId additions

Add `AutomationProperties.AutomationId` to these elements in `MainOverlayWindow.xaml`:

| Element | AutomationId |
|---|---|
| Start/Stop button | `Btn_ToggleSession` |
| Stealth toggle button | `Btn_ToggleStealth` |
| Theme toggle button | `Btn_ToggleTheme` |
| History panel button | `Btn_ToggleHistory` |
| Settings button | `Btn_OpenSettings` |
| Sidebar toggle button (title bar ▶) | `Btn_ToggleSidebar` |
| Sidebar hide button (◀ Hide) | `Btn_HideSidebar` |
| Capture button | `Btn_Capture` |
| Mic status dot (Ellipse) | `StatusDot_Mic` |
| System status dot (Ellipse) | `StatusDot_System` |
| OCR status dot (Ellipse) | `StatusDot_OCR` |
| AI status dot (Ellipse) | `StatusDot_AI` |
| Mic dot HelpText (active state) | `AutomationProperties.HelpText` bound to `IsMicActive` → "active\|inactive" |
| System dot HelpText | same pattern for `IsSystemAudioActive` |
| OCR dot HelpText | same pattern for `IsOcrReady` |
| AI dot HelpText | same pattern for `IsAiConnected` |

Dot state is asserted via `AutomationProperties.HelpText` set to `"active"` or `"inactive"` using the existing `BoolToStringConverter`. FlaUI reads this as `element.Properties.HelpText.Value`.

---

## Part 3 — Test coverage per checklist section

### Startup (StartupTests.cs)
- `Log_ContainsSileroVadReady` — reads today's log file from `D:\AIHelperNET\logs\`, asserts contains "Silero VAD"
- `Log_ContainsWhisperModelReady` — asserts log contains "Whisper"

### UI Controls (UIControlTests.cs)
- `Mode_AudioOnly_Selects` / `Mode_ScreenOnly_Selects` / `Mode_AudioAndScreen_Selects`
- `AudioSource_MicOnly_Selects` / `AudioSource_SystemOnly_Selects` / `AudioSource_Both_Selects`
- `ScreenMode_AllSix_Select` — `[Theory]` over all 6 AutomationIds
- `ThemeToggle_DoesNotCrash` — click ◐ twice (light → dark), assert app window still exists and responds (theme correctness is a visual check, not assertable via UIA)
- `HistoryPanel_OpensAndCloses` — click 📋, assert `Panel_History` visible; click again, assert collapsed
- `Sidebar_HidesAndRestores` — click ◀ Hide, assert sidebar width = 0; click ▶, assert restored

### Session lifecycle (SessionLifecycleTests.cs)
- `Start_HeaderShowsListening` — click Start, assert title bar text contains "Listening"
- `Start_BothMode_MicAndSystemDotsGreen` — start in Both mode, assert `StatusDot_Mic` + `StatusDot_System` fill green
- `Start_MicOnly_OnlyMicDotGreen` — start in Mic Only, assert Mic green, System grey
- `Start_SystemOnly_OnlySystemDotGreen` — start in System Only, assert System green, Mic grey
- `Stop_HeaderShowsStopped` — stop session, assert "Stopped"
- `Stop_DotsGoGrey` — assert Mic + System dots return to grey

### Screen capture (ScreenCaptureTests.cs)
- `Capture_WithTestImage_TurnCardAppears` — open `tests/testImage/coding_question.png`, press Ctrl+Shift+S, wait up to 30 s for a turn card element to appear in the answer panel

### Settings (SettingsTests.cs)
- `Settings_WindowOpens` — click ⚙, assert a new window with title containing "Settings" appears
- `Settings_AudioTab_Visible` — assert Audio tab control exists
- `Settings_CodeProfilesTab_Visible` — assert Code Profiles tab exists
- `Settings_AppearanceTab_Visible` — assert Appearance tab exists

---

## Dot state detection strategy

WPF `Ellipse` elements expose their fill color through the `LegacyIAccessiblePattern.Description` — however this is not always populated. The reliable fallback is:

1. Add `AutomationProperties.HelpText="{Binding ..., Converter=BoolToStringConverter, ConverterParameter='active|inactive'}"` to each `Ellipse`, giving FlaUI a readable property to assert on.
2. Tests assert `helpText == "active"` for green and `"inactive"` for grey.

This is more reliable than trying to parse color values from FlaUI.

---

## Test isolation

- All UI tests run in a single `[Collection("UITests")]` collection — xUnit runs them sequentially against the shared app instance
- Each test class that starts a session must stop it in `IAsyncLifetime.DisposeAsync()`
- `ScreenCaptureTests` and `SettingsTests` close any child window they open before the next test class runs

---

## What is NOT automated

| Checklist item | Reason |
|---|---|
| Real microphone audio → transcript | No virtual audio device; covered by `SessionRunnerTests` (in-process) |
| Answer streams in within a few seconds | Requires live AI API key; integration-tested via mocks |
| Visual theme rendering correctness | Subjective visual check; not assertable via UIA |
| Settings window on secondary monitor (known bug) | Known issue, tracked separately |
