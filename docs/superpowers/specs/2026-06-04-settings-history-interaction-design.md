# AIHelperNET — Settings, History & Interaction Design

**Date:** 2026-06-04  
**Status:** Approved  
**Scope:** Three feature groups prioritized from feature gap analysis vs InterviewHelper

---

## Overview

Eleven user-facing features were prioritized during a gap analysis session. They are organized into three independent implementation groups ordered by architectural layer:

- **Group 1 — Settings & Configuration** (smallest, ships first)
- **Group 2 — Session Lifecycle** (history + export)
- **Group 3 — Overlay & Interaction** (follow-up questions, screen analysis, audio meters)

Deferred to backlog: session pause/resume, compact overlay strip.

---

## Group 1 — Settings & Configuration

### Features
1. Audio device selection UI (mic + loopback)
2. Whisper input language selection
3. Code Profile UI + Named Presets (Code Profile + Answer Settings)
4. Overlay opacity slider

### Domain / Settings Model Changes

**`AppSettingsDto`** gains three new fields:

```csharp
double OverlayOpacity          // default 0.75, range 0.2–1.0
string WhisperLanguage         // default "auto"; options: auto, en, ru, pl, <custom>
IReadOnlyList<ProfilePreset> Presets  // default empty list
```

**`ProfilePreset`** is a new value object:

```csharp
record ProfilePreset(
    string Name,
    CodeProfile CodeProfile,
    AnswerSettings AnswerSettings
);
```

`JsonSettingsStore` serializes `Presets` as a JSON array within the existing `settings.json` file. No new file or storage mechanism is needed.

### `ISettingsStore` — no interface change needed

Presets are stored as part of `AppSettingsDto`; loading and saving presets uses the existing `SaveAsync` / `LoadAsync` methods.

### SettingsWindow — 4 Tabs

The existing `SettingsWindow` modal is expanded with a `TabControl`. The API Key tab is unchanged.

---

#### Tab 1: API Key *(unchanged)*

---

#### Tab 2: Audio

```
┌─────────────────────────────────────────────────────────┐
│  Settings                                            [X] │
├────────────┬─────────┬───────────────┬──────────────────┤
│  API Key   │  Audio  │ Code Profiles │   Appearance     │
├────────────┴─────────┴───────────────┴──────────────────┤
│                                                          │
│  AUDIO DEVICES                                           │
│  Microphone   [Headset Mic (Realtek HD Audio)       ▼]  │
│  System Audio [WASAPI Loopback - Speakers           ▼]  │
│                                                          │
│  TRANSCRIPTION                                           │
│  Language     [Auto-detect                          ▼]  │
│               ( Auto  English  Russian  Polish  Custom ) │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**
- Device dropdowns are populated on tab open via `NAudio.CoreAudioApi.MMDeviceEnumerator` — one list for capture devices (microphone), one for loopback devices (system audio).
- Selected device IDs persist to `AppSettingsDto.MicDeviceId` and `AppSettingsDto.LoopbackDeviceId` on Save.
- `NAudioCaptureService` already accepts device IDs; no service changes required.
- Language dropdown persists to `AppSettingsDto.WhisperLanguage`. Custom option reveals a text field for a BCP-47 code.
- Language is passed to `WhisperTranscriptionService` on the next session start.

---

#### Tab 3: Code Profiles

```
┌─────────────────────────────────────────────────────────┐
│  Settings                                            [X] │
├────────────┬─────────┬───────────────┬──────────────────┤
│  API Key   │  Audio  │ Code Profiles │   Appearance     │
├────────────┴─────────┴───────────────┴──────────────────┤
│                                                          │
│  PRESETS    [C# Azure Backend               ▼] [Load]   │
│                                          [Delete preset] │
│  ──────────────────────────────────────────────────────  │
│                                                          │
│  TECH STACK                                              │
│  Language         [C#                         ▼]        │
│  Backend          [ASP.NET Core               ▼]        │
│  Frontend         [Angular                    ▼]        │
│  Database         [SQL Server                 ▼]        │
│  Cloud / DevOps   [Azure                      ▼]        │
│  Architecture     [Clean / Hexagonal          ▼]        │
│  Testing          [xUnit                      ▼]        │
│  Custom notes     [e.g. .NET 10, EF Core 9        ]     │
│                                                          │
│  ANSWER STYLE                                            │
│  Length       ( Short  ●Medium  Detailed  Deep dive )   │
│  Complexity   ( Simple  ●Balanced  Advanced  Senior )   │
│  Style        [Technical                      ▼]        │
│  Tone         [Professional                   ▼]        │
│  Format       [Explanation + Code             ▼]        │
│  Output lang  [Same as input                  ▼]        │
│                                                          │
│  Preset name  [C# Azure Backend                   ]     │
│         [Save as new preset]    [Update current]        │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**
- Presets dropdown lists all saved presets from `AppSettingsDto.Presets`.
- **Load** applies the selected preset's `CodeProfile` and `AnswerSettings` fields into the form and into the active `AppSettingsDto`.
- **Save as new preset** creates a new `ProfilePreset` with the name field value and appends it to `Presets`.
- **Update current** overwrites the currently selected preset in-place.
- **Delete preset** removes it from the list with a confirmation prompt.
- Changes take effect immediately for answer generation — no session restart required.
- The form edits the *active* `CodeProfile` and `AnswerSettings` directly (same fields `PromptBuilderService` already reads), so switching a preset mid-session affects the next generated answer.

---

#### Tab 4: Appearance

```
┌─────────────────────────────────────────────────────────┐
│  Settings                                            [X] │
├────────────┬─────────┬───────────────┬──────────────────┤
│  API Key   │  Audio  │ Code Profiles │   Appearance     │
├────────────┴─────────┴───────────────┴──────────────────┤
│                                                          │
│  OVERLAY                                                 │
│  Opacity   20%  ──────────●────────────────────  100%  │
│                           75%                           │
│                                                          │
│  (Preview updates live as you drag)                      │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**
- Slider range: 0.2 – 1.0. Bound two-way to `MainOverlayWindow.Opacity` for live preview.
- Value persists to `AppSettingsDto.OverlayOpacity` on Save.
- On app startup, `MainOverlayWindow.Opacity` is initialized from the stored value instead of the current hardcoded `0.75`.

---

## Group 2 — Session Lifecycle

### Features
1. Session history browser (in-overlay panel)
2. Session history export (TXT / Markdown)

### Navigation

A **History** button is added to the `MainOverlayWindow` title bar. Clicking it swaps the main content area (transcript + answers) for the history panel. Clicking again returns to the live session view. Switching to history while a session is active does not affect the session.

### History Panel

```
┌─────────────────────────────────────────────────────────┐
│  ← Live Session                            [Export...]  │
│  ─────────────────────────────────────────────────────  │
│  🔍 [Search sessions...                           ]     │
│                                                          │
│  ▶ 2026-06-04  14:32   AudioAndScreen   12 Q   C# Azure │
│  ▶ 2026-06-03  10:15   AudioOnly         8 Q   Angular  │
│  ▶ 2026-06-02  09:00   AudioOnly         5 Q   General  │
│                                                          │
│  ▼ 2026-06-01  11:45   AudioAndScreen    9 Q   SQL      │
│  ┌────────────────────────────────────────────────────┐ │
│  │ TRANSCRIPT                                         │ │
│  │ [14:32:01] Other: Can you explain CQRS?            │ │
│  │ [14:32:15] Me: CQRS stands for...                  │ │
│  │                                                    │ │
│  │ ANSWERS                                            │ │
│  │ Q: Can you explain CQRS?                           │ │
│  │ A: CQRS separates read and write models...         │ │
│  └────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**
- Session list is loaded from the existing `GetSessionHistoryQuery` (already implemented in the Application layer).
- Each row shows: date, time, mode, question count, and the preset/language name if stored.
- Rows are expandable — clicking a row fetches full transcript items and conversation turns for that session.
- Search filters by date or by text matching transcript content (simple LIKE query on existing SQLite data via a new `SearchSessionsQuery`).

### Export

"Export..." button in the history panel header opens a small inline dialog:

```
  Format:   ( ● TXT   ○ Markdown )
  Content:  ( ● Both  ○ Transcript only  ○ Answers only )

  [Cancel]                          [Save to Downloads]
```

**Behavior:**
- Default export path: user's `Downloads` folder.
- TXT format: plain text with timestamps and speaker labels.
- Markdown format: `## Session — 2026-06-04 14:32`, transcript as blockquotes, answers as headings + body text.
- A new `ExportSessionCommand` handler in the Application layer generates the file content from domain objects and writes to disk via a new `IExportService` port.
- Works for any session in the history list, including the current session.

---

## Group 3 — Overlay & Interaction

### Features
1. Follow-up questions per answer (session-level toggle)
2. Screen analysis modes in sidebar (always visible)
3. Audio level meters in sidebar

### Follow-up Questions

**Session-level toggle:** a "Follow-ups" toggle is added to the existing sidebar alongside the mode/source selectors. When enabled, a text input appears below each answer card.

```
┌─────────────────────────────────────────────────────────┐
│ Q: Can you explain CQRS?                           [✕][✓]│
│ A: CQRS separates read and write models. Commands       │
│    mutate state; queries return data without...          │
│                                                          │
│ [Ask a follow-up...                              ] [→]  │
└──────────────────────────────────────────────────────────┘
```

**Behavior:**
- When the toggle is off, follow-up inputs are hidden from all cards.
- When the toggle is on, a text input appears at the bottom of every answer card.
- Pressing Enter or [→] sends a new `GenerateAnswerCommand` with the follow-up text. The command handler injects the original question + current answer text as context prefix in the prompt before the follow-up question.
- The response creates a new `AnswerVersion` on the same `ConversationTurn` (type: `FollowUp` — a new value to add to the `AnswerVersionType` enum), displayed as the latest version on the card.
- The follow-up input clears after submission.

### Screen Analysis Modes (Sidebar)

A new **Screen Analysis** section is always visible in the sidebar, below the existing mode/source selectors. The Ctrl+Shift+S hotkey triggers capture using whichever mode and context settings are currently selected.

```
┌─────────────────────┐
│  SCREEN ANALYSIS    │
│  ─────────────────  │
│  ● General          │
│  ○ Solve coding     │
│  ○ Debug error      │
│  ○ Explain code     │
│  ○ System design    │
│                     │
│  ☑ + interviewer    │
│    context (5 lines)│
│                     │
│  [📷 Capture]       │
└─────────────────────┘
```

**Behavior:**
- Selected mode and checkbox state are held in `SessionControlViewModel` (not persisted — reset to General + checked on each app launch).
- When Capture is triggered (button or Ctrl+Shift+S), the current mode is passed to `CaptureScreenCommand`.
- `PromptBuilderService` selects a system prompt template based on the mode:
  - **General** — existing generic screen context prompt
  - **Solve coding task** — "Given this coding task, provide a complete working solution in the candidate's tech stack"
  - **Debug error** — "Analyze this error/stack trace and explain the root cause and fix"
  - **Explain code** — "Explain what this code does, its patterns, and any notable design decisions"
  - **System design** — "Provide a high-level system design approach for the requirements shown"
- When "interviewer context" is checked, the last 5 transcript items where `Speaker == Other` are prepended to the prompt as conversation context.

### Audio Level Meters (Sidebar)

A new **Audio** section is always visible in the sidebar showing real-time signal bars for mic and loopback.

```
┌─────────────────────┐
│  AUDIO              │
│  ─────────────────  │
│  🎤 Mic             │
│  ████░░░░░░  42%    │
│                     │
│  🔊 System          │
│  ██░░░░░░░░  18%    │
└─────────────────────┘
```

**Behavior:**
- A new lightweight `AudioLevelMonitor` service runs independently of the session pipeline. It opens the selected mic and loopback devices in a read-only monitoring mode and emits `AudioLevelChanged` events (peak level, 0.0–1.0) approximately every 100ms per channel.
- The monitor starts when the app starts and stops when the app exits — it is always running, not tied to session state. This allows the user to verify device signal before starting a session.
- A new `AudioLevelViewModel` subscribes to these events and exposes `MicLevel` and `SystemLevel` as bindable double properties.
- The sidebar progress bars bind to these properties and animate in real time at all times.

---

## Implementation Order

Each group is independent and can be implemented sequentially:

1. **Group 1** — All changes are in `App`, `Infrastructure`, and `AppSettingsDto`. No domain changes. Smallest scope.
2. **Group 2** — New Application layer query + command + port. New UI panel. No audio or AI changes.
3. **Group 3** — UI additions to `MainOverlayWindow` sidebar + `PromptBuilderService` mode dispatch + lightweight audio level monitoring.

---

## Out of Scope (Deferred to Backlog)

- Session pause / resume
- Compact overlay strip
- Session tagging and notes
- Answer rating
- Clipboard history
- Transcript bookmarks
- Session replay
- Rebindable hotkeys
- Whisper model downloader UI
- Notification system
