# Multi-Screen-Capture Aggregation — Design

**Date:** 2026-06-10
**Branch:** `feature/multi-screen-capture` (off `develop`)
**Scope:** Let several screen captures of one long coding question be answered as a single
card over their combined OCR, and raise the screen-capture token budget.

This is an **App + Application** change. No Domain entity change beyond reusing existing
operations; no EF schema change — **no migration**. (Touches `ConversationTurnViewModel.cs`,
which PR #30 also edits in different methods — expect a small merge once #30 lands.)

---

## Problem

Today a screen capture either creates a "[Screen capture]" turn (first capture) or calls
`RegenerateAnswerWithScreenCommand` on the *latest* turn — using **only the newest capture's
OCR**, discarding earlier captures. So a long problem that needs scrolling across several
captures loses content, and a capture can land on an unrelated (e.g. audio) turn because the
target is `Turns.FirstOrDefault()`. Screen answers are also capped at a 500-token floor, which
truncates coding solutions.

## Desired behavior (locked in brainstorming)

- **Generate on every capture.** Each capture immediately (re)generates the answer.
- **Gap-based grouping.** Consecutive captures less than `N ≈ 8s` apart belong to the same task;
  each new capture **cancels the in-flight screen generation** and **regenerates over the full
  accumulated OCR**, refining the **same card** (a new answer version).
- **New task on a gap ≥ N** (or on Dismiss/Resolve) → a fresh card.
- **Combined OCR** = labeled concatenation of each capture's OCR (`--- Screen 1 ---` …); the model
  reconciles overlap.
- **Token floor 500 → 1000** for screen-mode answers.

## Non-goals

- OCR overlap de-duplication / stitching (labeled concatenation only).
- LLM-judged or content-overlap relatedness (time-gap only).
- A configurable gap setting UI (the gap is a named constant; tunable in code).
- Changing the audio/transcript pipeline.

---

## Components

### 1. `ScreenCaptureAccumulator` (Application — pure, unit-tested)

`src/AIHelperNET.Application/Answers/ScreenCaptureAccumulator.cs`. Owns the OCR buffer and the
grouping decision; no WPF or timer code.

- ctor: `ScreenCaptureAccumulator(TimeSpan gap)` (default gap supplied by the caller; the App
  uses a named `8s` constant).
- `ScreenCaptureAddResult Add(string ocr, DateTimeOffset now)`:
  - Starts a **new group** when there is no active group **or** `now - lastCaptureAt >= gap`:
    clears the buffer, `IsNewGroup = true`.
  - Otherwise appends, `IsNewGroup = false`.
  - Appends `ocr`, sets `lastCaptureAt = now`, returns
    `(bool IsNewGroup, string CombinedOcr, int Count)`.
- `CombinedOcr` = each buffered capture joined as:
  `--- Screen 1 ---\n<ocr1>\n\n--- Screen 2 ---\n<ocr2>` …
- `void Reset()` — clears the buffer and active-group state (called on Dismiss/Resolve).

`ScreenCaptureAddResult` is a small record `(bool IsNewGroup, string CombinedOcr, int Count)`.

Pure (`now` injected), so grouping + concatenation are fully testable with a fixed clock.

### 2. Command shape (Application)

The "regenerate over the accumulated set on the same card" behavior needs the card's turn id
**immediately** (before the first answer finishes streaming). So split the first-capture path:

- **New `CreateScreenTurnCommand(SessionId) : IRequest<Result<ConversationTurnId>>`** — creates
  and persists the `[Screen capture]` turn, notifies the UI via `IConversationTurnSink`, and
  returns the new `ConversationTurnId` immediately (no streaming). It must leave the turn in the
  same status from which the existing screen-answer flow validly calls `StartAnswer`
  (mirror the status the current `StartScreenTurnHandler` is in right before its `StartAnswer`).
- **Reuse `RegenerateAnswerWithScreenCommand(SessionId, TurnId, ScreenContext, Mode,
  InterviewerLines)`** for **both** the first and every subsequent generation. It already starts
  an answer, streams an `UpdatedWithScreen` version onto the turn, transitions
  `GeneratingRefined → RefinedReady`, and handles `OperationCanceledException` (→ `answer.Cancel()`,
  no error card). It is cancellable via the token passed to `mediator.Send`.
- **Retire `StartScreenTurnCommand`** (and its handler) if `CaptureScreenAsync` was its only
  caller; otherwise leave it. Check for other references (including tests) before removing.

**Verification point for the plan:** confirm `CreateScreenTurnCommand` + `RegenerateAnswerWithScreen`
produces the same valid `ConversationTurnStatus` sequence the current single-shot
`StartScreenTurnHandler` produces (Detected → … → RefinedReady). If `Detected → GeneratingRefined`
is not a valid transition, `CreateScreenTurnCommand` sets the appropriate pre-answer status.

### 3. VM orchestration (`ConversationTurnViewModel`, App)

`CaptureScreenAsync` is rewritten to drive the accumulator + cancellation. New VM state:
- `ScreenCaptureAccumulator _screenAccumulator` (gap = `ScreenCaptureGroupGap = TimeSpan.FromSeconds(8)`).
- `ConversationTurnId? _screenGroupTurnId`.
- `CancellationTokenSource? _screenGenCts`.
- Inject `TimeProvider clock` into the VM ctor (for `now`).

Per capture:
1. `ocr = await CaptureScreenCommand` — on failure, return (don't disturb the group).
2. `result = _screenAccumulator.Add(ocr, clock.GetUtcNow())`.
3. `_screenGenCts?.Cancel(); _screenGenCts?.Dispose(); _screenGenCts = new();`
4. If `result.IsNewGroup` → `_screenGroupTurnId = null` (force a fresh card).
5. If `_screenGroupTurnId is null` (new group **or** a prior `CreateScreenTurnCommand` failed and
   left no turn): `var created = await mediator.Send(new CreateScreenTurnCommand(sid));` — on
   success `_screenGroupTurnId = created.Value;` else return (the accumulator keeps the buffer, so
   the next capture retries the create).
6. `await mediator.Send(new RegenerateAnswerWithScreenCommand(sid, _screenGroupTurnId.Value,
   result.CombinedOcr, mode, lines), _screenGenCts.Token);` (cancellation of the prior capture's
   token makes its awaited send return via the handler's OCE path — no error card).
6. `mode`/`lines` resolved exactly as today (from the `SessionControlViewModel`).

`Dismiss`/`Resolve` on a turn call `_screenAccumulator.Reset()` and clear `_screenGroupTurnId`
if that turn was the group's turn.

This replaces the old `Turns.FirstOrDefault()` targeting, fixing the audio-turn-hijack quirk.

### 4. Capture-count affordance (App, low-risk)

The screen turn's displayed label reflects the running count, e.g. `[Screen capture · 3 screens]`,
updated as captures accumulate. Make `TurnVm.InitialQuestion` settable/observable (today it is a
ctor-only get) OR expose a separate observable `ScreenCaptureLabel`; the VM updates it on each
capture from `result.Count`. Keep it minimal.

### 5. Token floor (Application)

`PromptBuilderService.BuildWithScreenMode`: change
`MaxTokens: Math.Max(MapLengthToTokens(settings.Length), 500)` → `... , 1000)`.

---

## Data flow

```
Hotkey → CaptureScreenAsync
  → OCR (CaptureScreenCommand)
  → accumulator.Add(ocr, now) → (IsNewGroup, CombinedOcr, Count)
  → cancel previous _screenGenCts; new cts
  → if IsNewGroup: _screenGroupTurnId = null
  → if _screenGroupTurnId is null: turnId = await CreateScreenTurnCommand   (creates card)
  → await RegenerateAnswerWithScreenCommand(turnId, CombinedOcr, …, cts.Token)  (streams onto card)
```

## Error handling

- Superseded generation → `OperationCanceledException` in the handler → `answer.Cancel()`, **no
  error card**.
- OCR failure → skip this capture, group untouched.
- `CreateScreenTurnCommand` failure → abort this capture; the next capture retries (accumulator
  still holds the buffer; it will attempt create again because `_screenGroupTurnId` stayed null).
- Provider errors during streaming → existing `AnswerErrorMessage.ForUser` error card (unchanged).

## Testing

- **`ScreenCaptureAccumulator`** (xUnit, `Application.Tests`, fixed clock): first capture =
  new group; second within gap = continuation; capture at exactly/after gap = new group;
  `CombinedOcr` labeling + order + count; `Reset()` starts a fresh group next.
- **`BuildWithScreenMode`** (existing test file): `MaxTokens >= 1000`; VeryShort/Short hit the
  1000 floor; Detailed/DeepDive still map higher (1000/2000).
- **VM orchestration** (timers, cancellation, turn-id, count label): App layer — **live/manual**
  verification (rapid OCR captures aren't easily FlaUI-driven). The cancel-and-regenerate +
  create-then-regenerate flow is the main risk; verify manually with multi-capture scenarios.

## Success criteria

- Capturing several screens < 8s apart → **one card** whose answer reflects the **combined** OCR,
  regenerating on each capture; the card shows the running screen count.
- A capture after a ≥ 8s gap → a **new card**.
- Screen capture targets its own card, never an unrelated audio turn.
- Screen-capture answers get a **≥ 1000-token** budget.
- `dotnet build` clean (warnings-as-errors); existing suites green; accumulator fully unit-tested.

## Risks

- **Concurrency / turn-id timing** — mitigated by awaiting the fast `CreateScreenTurnCommand`
  before the first `Regenerate`, so the turn id is known synchronously; subsequent captures reuse it.
- **Status-transition validity** of the create-then-regenerate path — explicit verification point
  in the plan (mirror the current handler's transitions).
- **Token cost** of superseded generations — accepted per the chosen behavior; bounded by the
  per-answer `MaxTokens` cap.
- **Prompt-injection surface unchanged** — OCR stays fenced/labeled as data; output remains
  display-only.
