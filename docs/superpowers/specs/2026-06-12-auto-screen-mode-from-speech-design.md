# Auto-Select Screen Analysis Mode from Spoken Intent — Design

**Date:** 2026-06-12
**Status:** Approved (pending spec review)

## Problem

When the interviewer asks a task out loud — "write a SQL to get students with access to
more than 2 locations", "fix the bug", "explain this code" — and then shows the relevant
content on screen, the candidate must **manually** click the correct screen-analysis mode
toggle before capturing. If they forget, the capture runs in `General` mode and the answer
is vaguer (no solution-first / debug-first framing). The spoken question already contains
the intent; the app should read it and pre-select the matching mode.

## Goal

Automatically set `SessionControlViewModel.ScreenAnalysisMode` from the interviewer's most
recent speech, so a screen capture taken right after a spoken instruction uses the right
mode without a manual click. Manual toggling continues to work.

## Scope

**In scope** — auto-select these four modes from matching phrases:

| Mode | Trigger phrases (case-insensitive, examples) |
|---|---|
| `DebugError` | "fix the bug", "fix this bug", "fix the error", "debug this", "what's wrong with", "why is this failing", "why does this fail" |
| `ExplainCode` | "explain this code", "explain the code", "what does this code do", "walk me through this code" |
| `SystemDesign` | "design a system", "system design", "how would you design", "architect this", "design the architecture" |
| `SolveCodingTask` | "write a sql", "write sql", "write a query", "write a code", "write code", "write a function", "write a method", "implement", "solve this task", "solve the task", "code this" |

**Out of scope (YAGNI):**
- No AI classifier — deterministic keyword matching only (zero latency, zero cost, testable).
- No `MultipleChoice` / `General` auto-detect (can't be triggered reliably from speech).
- No persistence, no settings toggle to enable/disable, no per-phrase configuration UI.

## Architecture

Two pieces, following the existing pure-helper pattern (`ScreenFollowUpRouter`,
`ScreenCaptureAccumulator`):

### 1. `ScreenModeClassifier` — Application layer, pure/static

```csharp
namespace AIHelperNET.Application.Answers;

public static class ScreenModeClassifier
{
    /// Returns the matching mode on a confident keyword hit, or null for "no signal".
    public static ScreenAnalysisMode? Classify(string interviewerText);
}
```

- Pure function, no I/O, no dependencies — fully unit-testable.
- Case-insensitive matching over `interviewerText`.
- **Ordering matters:** evaluate `DebugError`, then `ExplainCode`, then `SystemDesign`,
  then `SolveCodingTask`. This keeps "fix the **code**" / "explain the **code**" from
  falling through into the generic coding bucket.
- Returns `null` when nothing matches — the caller then leaves the current mode untouched.

### 2. Wiring — App layer

- New method on `SessionControlViewModel`:

  ```csharp
  /// Runs ScreenModeClassifier over the latest interviewer line and, only on a
  /// non-null result, updates ScreenAnalysisMode. A null result leaves it unchanged.
  public void AutoSelectScreenMode(string interviewerText);
  ```

- In `App.OnStartup`, the transcript sink already handles each new line and, for
  `Speaker.Other`, calls `turnVm.UpdateInterviewerLines(...)` (`App.xaml.cs:72-79`).
  Resolve the `SessionControlViewModel` singleton there and, in the same `Other` branch,
  call `sessionControl.AutoSelectScreenMode(item.Text)` with the new line's text.

## Data flow

```
Interviewer speaks  →  Whisper transcribes  →  TranscriptSink handler (Speaker.Other)
   →  turnVm.UpdateInterviewerLines(last5)         [existing]
   →  sessionControl.AutoSelectScreenMode(item.Text)   [new]
        →  ScreenModeClassifier.Classify(text)
             →  non-null  →  ScreenAnalysisMode set (toggle in overlay flips)
             →  null      →  no change
Candidate presses capture hotkey
   →  CaptureScreenAsync reads sessionControl.ScreenAnalysisMode   [existing]
   →  the already-correct mode is used to build the prompt
```

## Override behavior — latch on positive match

The mode only ever **changes** on a positive match; it never auto-resets to `General`.
Both the spoken signal and a manual toggle write the same property, so the **most recent
signal wins**:

- Interviewer says "write a SQL" → latches `SolveCodingTask`. Candidate captures → correct.
- If the candidate then manually picks a different mode, that sticks until the next
  matching spoken instruction.

This is intentional: continuous chit-chat (which classifies to `null`) never disturbs a
previously selected mode.

## Error handling

- Empty/whitespace text → `Classify` returns `null` (no change).
- The classifier cannot throw (pure string matching); no try/catch needed at the call site.
- Wiring runs on the UI thread inside the existing transcript handler (already marshaled),
  so setting the observable property is thread-safe.

## Testing

- **`ScreenModeClassifierTests`** (`AIHelperNET.Application.Tests`):
  - One positive case per mode family (≥2 phrasings each).
  - Ordering guard: "fix the code" → `DebugError` (not `SolveCodingTask`);
    "explain the code" → `ExplainCode`.
  - Negative cases: conceptual / chit-chat lines → `null`
    (e.g. "tell me about your experience", "what is a primary key").
  - Case-insensitivity: "WRITE A SQL" → `SolveCodingTask`.
- **App-layer wiring** (`SessionControlViewModel.AutoSelectScreenMode` + `App.xaml.cs`)
  is a trivial 2-line latch over the tested classifier. It is **not** unit-tested in
  isolation: `SessionControlViewModel`'s constructor pulls in concrete heavy services
  (`SessionRunner` → `TranscriptPipelineService`) that aren't substitutable, so a VM unit
  test would be disproportionately costly. The latch (`if (mode is not null) ... = mode`)
  carries no logic the pure classifier tests don't already cover; it is verified by manual
  smoke (speak a trigger → toggle flips; speak chit-chat → toggle unchanged).

## Files touched

- **New:** `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs`
- **New:** `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs`
- **Edit:** `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` (add method)
- **Edit:** `src/AIHelperNET.App/App.xaml.cs` (resolve `SessionControlViewModel`, call in
  the `Other` branch)

No Domain, Infrastructure, EF, or settings changes. App-layer wiring + one pure Application service.
