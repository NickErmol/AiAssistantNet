# Session Modes, Live Transcript, and ConversationTurn Design

**Date:** 2026-06-03
**Scope:** Gap items 1, 3, 4 from spec gap analysis
**Status:** Approved

---

## 1. Overview

This spec covers three interconnected features required to reach a working P0/P1 MVP:

1. **Session modes** — `AudioOnly`, `ScreenOnly`, `AudioAndScreen`, changeable mid-session
2. **Live transcript UI** — both `Me` and `Other Speaker` speech displayed in real time
3. **ConversationTurn model** — clarification-aware turn tracking with answer versioning

The three features share a single delivery because session mode determines what gets captured, captured audio feeds the transcript, and the transcript feeds the ConversationTurn lifecycle.

---

## 2. UI Design

### 2.1 Window

`MainOverlayWindow` replaces the existing `OverlayWindow`. Same stealth behaviour (`WDA_EXCLUDEFROMCAPTURE` set in `OnSourceInitialized`), `WindowStyle="None"`, `Topmost="True"`, `Opacity="0.75"`, user-resizable.

`OverlayWindow` is deleted.

### 2.2 Layout (Option C — Sidebar + Stacked Content)

```
Window (Opacity=0.75, Topmost, WindowStyle=None, ResizeMode=CanResizeWithGrip)
├── TitleBar (drag handle, session status chip, [Start] [Stop] [⚙] [Ctrl+Shift+H])
└── DockPanel
    ├── Sidebar (DockPanel.Dock=Left, collapsible via width animation to 0)
    │   ├── Mode selector  — Audio Only / Screen Only / Audio+Screen (radio buttons)
    │   ├── Audio source   — Mic Only / System Only / Both (radio buttons)
    │   ├── Status dots    — Mic ● System ● OCR ● AI ●
    │   └── [◀ Hide] button at bottom
    └── Grid (2 rows with GridSplitter)
        ├── Row 0 — TranscriptPanel (binds TranscriptViewModel)
        │           ScrollViewer, auto-scroll, Me/Other labels + timestamps
        ├── GridSplitter (horizontal, draggable)
        └── Row 1 — AnswerPanel (binds ConversationTurnViewModel)
                    Latest answer prominent, version history below (collapsed by default)
                    [Copy] [Regen] [Dismiss] [Resolve] per turn
```

Sidebar toggle: clicking `◀ Hide` animates sidebar width to 0; a `▶` button appears in the title bar to restore it. Both `SessionMode` and `AudioSourceMode` are changeable while a session is running — changing them fires `ChangeModeCommand`.

---

## 3. Domain Layer

### 3.1 New Enums on `Session`

```csharp
public enum SessionMode
{
    AudioOnly,
    ScreenOnly,
    AudioAndScreen
}

public enum AudioSourceMode
{
    MicrophoneOnly,
    SystemAudioOnly,
    Both
}
```

Both are added as properties to the `Session` aggregate root. A new domain method `ChangeMode(SessionMode, AudioSourceMode)` validates the transition (mode can only be changed on an active session) and updates the properties.

### 3.2 `ConversationTurn` Entity

Owned by `Session` (OwnsMany in EF Core).

```csharp
public sealed class ConversationTurn
{
    public ConversationTurnId Id { get; }
    public SessionId SessionId { get; }
    public QuestionId InitialQuestionId { get; }
    public string InitialQuestionText { get; }
    public ConversationTurnStatus Status { get; private set; }
    public IReadOnlyList<TranscriptItemId> ClarificationQuestionIds { get; }   // from Me
    public IReadOnlyList<TranscriptItemId> ClarificationResponseIds { get; }   // from Other
    public IReadOnlyList<AnswerVersion> AnswerVersions { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
}
```

**Status enum:**
```
Detected → GeneratingPreliminary → PreliminaryReady
→ AwaitingClarification → ClarificationReceived
→ GeneratingRefined → RefinedReady
→ Dismissed | Resolved
```

Domain methods: `AttachClarificationQuestion`, `AttachClarificationResponse`, `AddAnswerVersion`, `Dismiss`, `Resolve`, `TransitionTo(status)`.

### 3.3 `AnswerVersion` Value Object

```csharp
public sealed record AnswerVersion(
    AnswerVersionId Id,
    AnswerVersionType VersionType,
    string Text,
    DateTimeOffset CreatedAt,
    AnswerVersionId? SupersedesId
);

public enum AnswerVersionType
{
    Preliminary,
    RefinedAfterClarification,
    UpdatedWithScreen,
    ManuallyRegenerated
}
```

`GeneratedAnswer` (streaming entity) is retained for the live streaming pipeline. When streaming completes, a snapshot `AnswerVersion` is appended to the parent `ConversationTurn`.

---

## 4. Application Layer

### 4.1 Updated Commands

**`StartSessionCommand`** gains `SessionMode Mode` and `AudioSourceMode AudioSource` parameters.

**`GenerateAnswerCommand`** targets `ConversationTurnId` instead of bare `QuestionId`. On completion, the handler appends an `AnswerVersion(Preliminary)` to the turn and transitions status to `PreliminaryReady`.

### 4.2 New Commands

| Command | Purpose |
|---|---|
| `ChangeModeCommand(SessionId, SessionMode, AudioSourceMode)` | Update mode mid-session; handler restarts affected capture services |
| `RegenerateAnswerCommand(SessionId, ConversationTurnId)` | Re-trigger generation, append `ManuallyRegenerated` version |
| `DismissTurnCommand(SessionId, ConversationTurnId)` | Mark turn Dismissed |
| `ResolveTurnCommand(SessionId, ConversationTurnId)` | Mark turn Resolved |

### 4.3 Transcript Pipeline Turn Lifecycle

Only one `ConversationTurn` is **active** at a time — defined as the most recent turn whose status is not `Dismissed` or `Resolved`. The pipeline applies the following rules on each new `TranscriptItem`:

**Other Speaker item arrives:**
1. Run `QuestionDetector`.
2. If question detected AND no active turn → create `ConversationTurn`, status `Detected`, fire `GenerateAnswerCommand` (async).
3. If question detected AND active turn is `AwaitingClarification` → attach as clarification response, transition to `ClarificationReceived`, fire `GenerateAnswerCommand` for refined answer.

**Me item arrives:**
1. If no active turn, or active turn is not `PreliminaryReady` → ignore for turn purposes.
2. Run `QuestionDetector` on `Me` speech.
3. If clarification question detected → attach to active turn, transition to `AwaitingClarification`.

### 4.4 Updated `IAnswerStreamSink`

```csharp
public interface IAnswerStreamSink
{
    void OnChunk(ConversationTurnId turnId, AnswerVersionType versionType, string chunk);
    void OnComplete(ConversationTurnId turnId, AnswerVersionType versionType);
    void OnError(ConversationTurnId turnId, string error);
}
```

---

## 5. Presentation Layer

### 5.1 ViewModels

**`SessionControlViewModel`**
- `SessionMode Mode` — bindable, fires `ChangeModeCommand` on set
- `AudioSourceMode AudioSource` — bindable, fires `ChangeModeCommand` on set
- `bool IsSessionActive`, `bool IsSidebarVisible`
- `bool IsMicActive`, `bool IsSystemAudioActive`, `bool IsOcrReady`, `bool IsAiConnected`
- Commands: `StartCommand`, `StopCommand`, `ChangeModeCommand`, `ToggleSidebarCommand`

**`TranscriptViewModel`**
- `ObservableCollection<TranscriptItemVm> Items`
- `TranscriptItemVm`: `Speaker` (Me/Other), `Text`, `Timestamp`
- Auto-scrolls to bottom on new item
- Items appended via a new `ITranscriptSink` interface (separate from `IAnswerStreamSink`); the audio transcription pipeline calls it after each `TranscriptItem` is committed

**`ConversationTurnViewModel`**
- `ObservableCollection<TurnVm> Turns` — active turn first
- `TurnVm`: `InitialQuestion`, `Status`, `StatusLabel`, `AnswerVersions`
- `AnswerVersionVm`: `VersionType`, `VersionLabel`, `Text`, `CreatedAt`, `IsLatest`
- Latest `AnswerVersionVm` displayed expanded; previous versions in a collapsible history
- Commands: `RegenerateCommand`, `DismissCommand`, `ResolveCommand`, `CopyLatestCommand`, `CaptureScreenCommand`

**`SettingsViewModel`** — unchanged for this iteration (expanded in a later gap).

### 5.2 `AnswerStreamSink` Update

`AnswerStreamSink` routes chunks to `ConversationTurnViewModel` by `ConversationTurnId` + `VersionType`. Live streaming appends to the latest `AnswerVersionVm.Text` in real time.

### 5.3 Hotkey Rewiring

All five hotkeys rewired to `SessionControlViewModel` and `ConversationTurnViewModel`:

| Hotkey | New target |
|---|---|
| `Ctrl+Shift+Space` | `SessionControlViewModel.StartCommand` / `StopCommand` |
| `Ctrl+Shift+Q` | `ConversationTurnViewModel.RegenerateCommand` |
| `Ctrl+Shift+C` | `ConversationTurnViewModel.CopyLatestCommand` |
| `Ctrl+Shift+S` | `ConversationTurnViewModel.CaptureScreenCommand` |
| `Ctrl+Shift+H` | `SessionControlViewModel.ToggleSidebarCommand` — **behavior change from v1**: previously hid the window; now hides/shows the sidebar. The window is always visible while a session is active. |

---

## 6. Persistence

`ConversationTurn` is added to `AppDbContext` as `OwnsMany` on `Session`. `AnswerVersion` is owned by `ConversationTurn`. EF Core migration required.

`Session` gains `Mode` (int) and `AudioSource` (int) columns.

---

## 7. Out of Scope for This Iteration

- Settings window expansion (answer settings, code profile, device selection)
- Session export to files
- Device diagnostics
- Preset profiles
- Legal disclaimer / onboarding
- Screen region selection

---

## 8. Definition of Done

- `MainOverlayWindow` launches, stealth confirmed in log
- Sidebar collapses/expands; mode and audio source selectable mid-session
- Live transcript shows Me + Other items with speaker labels and timestamps as audio arrives
- Remote speaker question creates a ConversationTurn; preliminary answer streams into AnswerPanel
- Me clarification question transitions turn to AwaitingClarification
- Remote clarification response triggers refined answer; both versions visible in history
- All five hotkeys functional with new targets
- All existing tests pass; new unit tests cover ConversationTurn state machine
