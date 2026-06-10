# Screen-Task Interviewer Follow-ups — Design

**Date:** 2026-06-10
**Branch:** `feature/screen-task-followup` (off `develop`)
**Scope:** After a screen-capture answer card exists, when the **interviewer** (audio) adds a
condition to, or asks a question about, the captured task, automatically produce a **new card**
that answers with full captured-task context — either a direct answer or an updated solution.

This is an **App + Application** change. **No Domain entity change, no EF schema change — no
migration** (the captured context lives in pipeline memory + an in-process command, not the DB).

---

## Problem

The app has two independent answer paths that don't talk to each other:

- **Audio path** — `TranscriptPipelineService` classifies interviewer/candidate speech and fires
  text-prompt answers (`GenerateAnswerCommand` → `PromptBuilderService.Build`).
- **Screen path** — `ConversationTurnViewModel.CaptureScreenAsync` (hotkey) → OCR →
  `CreateScreenTurnCommand` + `RegenerateAnswerWithScreenCommand`
  (`PromptBuilderService.BuildWithScreenMode`).

Two facts break the desired behavior today:

1. **The captured OCR is never retained anywhere reusable.** A screen turn's question text is the
   literal `"[Screen capture]"`; the OCR lives only transiently in the VM's `_screenAccumulator`
   and is passed once to `RegenerateAnswerWithScreen`.
2. **The pipeline is blind to screen captures.** `SessionRunner.RunAsync` loads the session
   **once** and passes that one in-memory `Session` instance to every `pipeline.ProcessAsync`. The
   screen path runs in **separate DI scopes / separate `Session` instances** (`mediator.Send`), so
   the screen turn it creates is never visible in the pipeline's session. The pipeline's
   `session.ActiveTurn` is the last *audio* turn it made itself — never the screen turn. (This is
   the known "pipeline stale session" condition.)

So when the interviewer reacts to a captured task by voice, today the app either (a) appends the
words to the `"[Screen capture]"` turn and regenerates via the **text** path — mutating the card
and answering with **no OCR** — or (b) spins a context-free new card. Neither is useful.

## Desired behavior (locked in brainstorming)

- **Trigger: automatic, via the audio pipeline.** No manual hotkey.
- **Always a new card.** The original capture card (and any prior follow-up card) is **never
  mutated**.
- **Captured-task context.** Each follow-up card answers from: the captured **OCR** + **all
  accumulated interviewer additions so far** + the **most recent prior answer** in the lineage.
- **Accumulate all.** `Card A` (capture) → interviewer "make it thread-safe" → `Card B`
  (OCR + ["thread-safe"]) → interviewer "and handle nulls" → `Card C`
  (OCR + ["thread-safe","handle nulls"] + B's answer).
- **Answer or update — the model decides.** If the utterance adds a condition → emit the updated
  solution incorporating every accumulated requirement; if it's a question about the task/solution
  → answer it directly.
- **Lifetime.** Linkage lives while a screen task is the current focus; it ends when the
  interviewer clearly moves on (a new, unrelated question) or the card is dismissed/resolved. **No
  timer.**
- **Card title** = the captured-task **topic label** (Improvement B), not the raw utterance.

## Non-goals

- Persisting captured context to the DB / surviving a mid-session app restart (in-memory only).
- A manual hotkey/button trigger (automatic only).
- Changing the multi-capture grouping or the audio routing for non-screen turns.
- OCR de-duplication, LLM-judged relatedness beyond the follow-up classifier.
- A user setting to disable the feature, or an idle-staleness cap (deferred — see Deferred).

---

## Architecture: an in-memory screen-task bridge

Because the pipeline cannot see screen turns through the shared session, it keeps its **own**
small, in-memory record of the active screen task. This is the heart of the design.

```
// Held by TranscriptPipelineService (per session; cleared in Reset()).
sealed record ScreenTaskContext(
    ConversationTurnId ScreenCardId,   // the original capture card
    string TopicLabel,                 // one-line summary derived from OCR (Improvement B)
    string Ocr,                        // combined OCR of the captured task
    ScreenAnalysisMode Mode,
    IReadOnlyList<string> Additions,   // accumulated interviewer additions (capped)
    ConversationTurnId LatestCardId,   // most recent card in the lineage (parent for next follow-up)
    DateTimeOffset UpdatedAt);
```

### 1. Bridge in (VM → pipeline)

When a capture answer is produced, the VM notifies the pipeline of the current screen task. The
notification carries the **combined OCR**, the **card id**, the **mode**, and an **`isNewGroup`**
flag (from `ScreenCaptureAddResult.IsNewGroup`) so a fresh task resets `Additions`. Multi-capture
refinements of one card simply overwrite `Ocr` on the same `ScreenCardId` — grouping logic stays
entirely in the VM, untouched.

- **Contract:** a new method on the pipeline, `RegisterScreenTask(ConversationTurnId cardId,
  string ocr, ScreenAnalysisMode mode, bool isNewGroup, DateTimeOffset now)`.
- **Thread-safety / ordering:** `ProcessAsync` runs on the single merge-channel consumer thread;
  `RegisterScreenTask` is called from the UI thread. Guard `_currentScreenTask` with a `lock` (it
  is read at the top of `ProcessAsync` and written by the register call). A near-simultaneous
  capture + transcript line is an acceptable minor race (the interviewer speaking the same
  instant as a capture is unusual). *(Rigorous alternative if races ever bite: enqueue the
  register as an event on the same merge channel so it is strictly ordered with transcript items —
  out of scope for v1.)*
- **Topic label (Improvement B):** derived once per task from the OCR — first non-empty line,
  trimmed to ~120 chars (fallback: first ~120 chars). Used as the card title and as classifier
  context.

### 2. Detection + routing (pipeline)

At the top of the **interviewer** branch in `BuildCommandWithBoundaryAsync` (after the existing
`Speaker.Me` short-circuit), if `_currentScreenTask` is set:

Run **one focused follow-up classification** of the utterance against the screen task (reusing the
`IQuestionBoundaryClassifier`, but with the screen **TopicLabel** supplied as the active-question
context and a synthetic `RefinedReady` status — so "added requirement vs. new question" is judged
relative to the captured task, not a stale audio turn). Map the result to three outcomes:

| Outcome | Meaning | Action |
|---|---|---|
| **Follow-up** | `AdditionalRequirement`, `ClarificationOfCurrentQuestion`, or post-collection `QuestionContinued` | Create a follow-up card; accumulate; **return** (skip normal routing) |
| **Moved on** | `NewQuestion` / unrelated complete question | **Clear** `_currentScreenTask`; fall through to today's normal audio routing |
| **Noise** | `NoQuestion` / `Unrelated` | Ignore (no card); return |

All non-screen (pure audio) routing is **unchanged** — the screen branch is unreachable unless
`_currentScreenTask` is set.

### 3. Creating the follow-up card

On a **Follow-up** outcome the pipeline:

1. **Immediately** accumulates the utterance: `_currentScreenTask.Additions =
   cap(_currentScreenTask.Additions + item.Text)`. No card/command yet.
2. **Touches a per-screen-task debounce** (Improvement A, reuse the `RegenDebouncer` pattern). The
   steps below run only when the debounce **fires**, so a burst of utterances within the window
   accumulates into the *same* set of additions and produces **one** card.
3. On debounce fire — create a new `ConversationTurn` **in the pipeline's own session/uow**
   (synchronously, like `HandleNewQuestion`), title = `TopicLabel`; announce via
   `turnSink.OnTurnCreated(newId, TopicLabel)` (the VM shows the card through existing wiring).
4. Fire `GenerateScreenFollowUpCommand` carrying the *current* accumulated additions (via the
   per-turn-CTS `FireAndForget`, so dismiss/resolve and supersession cancel it like any turn).
5. Update `_currentScreenTask.LatestCardId = newId` (the parent for the next follow-up).

This means rapid layered conditions ("make it thread-safe… and handle nulls") within one debounce
window yield a single card carrying both; a condition that arrives after the window starts a new
card that still accumulates on top.

### 4. The new command + handler

```
GenerateScreenFollowUpCommand(
    SessionId, ConversationTurnId TurnId, ConversationTurnId ParentTurnId,
    string Ocr, ScreenAnalysisMode Mode, IReadOnlyList<string> Additions,
    IReadOnlyList<string> RecentTranscript)   // Improvement C
```

`GenerateScreenFollowUpHandler` (mirrors `RegenerateAnswerWithScreenHandler`):

- Loads the session; finds `TurnId`.
- Reads the **prior answer** from `ParentTurnId`'s latest answer version — **best-effort**: omit
  if missing, incomplete, or `IsError` (OCR + additions still fully specify the task; § Edge cases
  1/6). Cap to ~1200 chars (§ Edge case 5).
- Builds the prompt via the new `PromptBuilderService.BuildScreenFollowUp(...)`.
- Streams as a new `AnswerVersion` of a new type `AnswerVersionType.ScreenFollowUp`; transitions
  `GeneratingRefined → RefinedReady`; saves.

### 5. Prompt (`BuildScreenFollowUp`)

- **System:** the captured task's `ModeSystemPrompt(mode)` (so a coding task stays a coding task) +
  shared markdown rule + the no-padding rule + the **decision instruction**:
  > *"The interviewer has added requirements to, or asked about, the task on screen. If they added
  > conditions, give the UPDATED solution incorporating ALL listed requirements (complete and
  > runnable). If they asked a question about the task or your approach, answer it directly and
  > briefly. Do not restate the task. Decide which from their words."*
- **User (all fenced + labeled as data — security rule):** `On-screen task (OCR):` block;
  `Interviewer requirements (most recent last):` numbered additions; optional
  `Recent conversation:` window (Improvement C, last few transcript lines, both speakers labeled);
  optional `Your previous answer:` block.
- **MaxTokens:** keep the screen `Math.Max(MapLengthToTokens(length), 2000)` floor (an updated
  full solution can be large).

---

## Data-flow walkthrough

1. Capture → Card A; VM calls `RegisterScreenTask(A, ocr, mode, isNewGroup:true)`.
   `_currentScreenTask = {A, topic, ocr, mode, [], A}`. A is the focus.
2. Interviewer "now make it thread-safe" → focused classifier ⇒ **Follow-up** → (debounce) Card B,
   `additions=["thread-safe"]`, prompt = OCR + ["thread-safe"] + A's answer. `LatestCardId=B`.
3. Interviewer "and handle nulls" → **Follow-up** → Card C, `additions=["thread-safe","handle
   nulls"]`, prompt = OCR + both + B's answer. `LatestCardId=C`.
4. Interviewer "ok, next — explain CAP theorem" → **Moved on** → `_currentScreenTask` cleared →
   normal audio card, no screen context.

---

## Compatibility (verified against current code)

- **Multi-capture → one card** — unchanged. Grouping stays in the VM's `_screenAccumulator` /
  `_screenGroupTurnId`. The only addition is the one-way `RegisterScreenTask` notification; repeated
  refinements of one card just overwrite the bridge's `Ocr` for the same card id.
- **Audio: several utterances of one context → one evolving card** — unchanged. The screen branch
  only runs when `_currentScreenTask` is set; pure-audio routing is byte-for-byte the same.
- **`Me` (candidate) speech** — still never generates; recent lines feed the follow-up prompt as
  context only (Improvement C), consistent with the conversation-routing model.

## Edge cases & resolutions

1. **Follow-up before parent finished streaming** → prior answer best-effort omitted.
2. **Rapid successive additions** → coalescing debounce (Improvement A) merges into one card.
3. **Dismiss/resolve mid-stream** → fired via per-turn-CTS `FireAndForget`; cancels like any turn.
4. **Unbounded additions** → cap the list (last ~8 / char budget); prior answer carries earlier ones.
5. **Huge prior answer** → cap to ~1200 chars.
6. **Parent answer errored / missing** → omit prior answer.
7. **Interviewer aside misread as a requirement** → bounded (dismissible); TopicLabel context
   (Improvement B) + the focused classifier reduce it.
8. **No topic for the classifier** → TopicLabel derived from OCR (Improvement B).
9. **Terse interviewer reply needing the candidate's question** → recent-transcript window
   (Improvement C).
10. **Manual re-capture after audio follow-ups branched** → manual captures form their own lineage
    on Card A; a manual re-capture refreshes the bridge to A, so later audio follows from the most
    recently touched task. Documented, not broken.
11. **Linkage end** → cleared on Moved-on or dismiss/resolve. (Idle cap deferred.)
12. **Two DB contexts writing one session** → pre-existing (screen + audio already coexist); this
    design adds only a normal new-turn write, no migration.

## Security

OCR, interviewer additions, and the recent-transcript window are **untrusted input**, kept fenced
and **labeled as data** in the prompt (per the standing rule). Output remains display-only — no
tool execution, file write, or network action driven by model output. Captured OCR lives only in
pipeline memory and the in-process command; it is **not** newly persisted and **not** logged.

## Testing

- **Unit — `BuildScreenFollowUp`:** mode system prompt present; OCR/additions/recent/prior all
  fenced + labeled; accumulation order; decision instruction present; 2000 token floor.
- **Unit — `GenerateScreenFollowUpHandler`:** reads parent answer; omits when incomplete/error;
  caps prior answer; streams a `ScreenFollowUp` version; `GeneratingRefined → RefinedReady`.
- **Pipeline — `TranscriptPipelineService`:** with `_currentScreenTask` set, interviewer
  "additional" ⇒ a **new** card with accumulated additions (capture card untouched);
  `NewQuestion` ⇒ `_currentScreenTask` cleared + normal audio card; noise ⇒ no card; `Me` ⇒ no
  generation. Debounce coalesces a burst into one card. Without `_currentScreenTask`, audio
  routing is unchanged (regression guard).
- **Topic label:** derivation from multi-line / empty OCR.
- No migration ⇒ no `MigrationTests` change.

## Deferred (not in this build)

- DB persistence / restart durability of the screen task.
- A setting to disable auto-follow-up (Improvement D).
- Idle-staleness cap on the linkage (Improvement E).
- Typed follow-up box OCR enrichment (separate small step).
- Feeding the boundary classifier a richer captured-task summary beyond the topic label.
