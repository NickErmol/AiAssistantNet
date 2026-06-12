# Answer-the-Latest-Question Hotkey (Ctrl+Shift+Z) — Design

**Date:** 2026-06-12
**Status:** Approved (brainstorming) — ready for implementation plan
**Branch:** `feature/answer-latest-question`

## Problem

The live pipeline occasionally **misses the latest question** in a discussion (e.g. a mistranscribed
or implicitly-phrased question), so no `ConversationTurn` is created and there is nothing to answer.
The existing **Generate answer** hotkey (`Ctrl+Shift+Q`) only *re-generates* the answer for the
most-recent **existing** turn (`RegenerateCommand` on `Turns.FirstOrDefault()`), so it cannot recover
a missed question.

We need a **manual fallback** the user can trigger to say: "look at what's been discussed recently,
figure out the latest question and its context, and answer it" — producing a normal answer card.

## Goal

A new global hotkey **`Ctrl+Shift+Z`** ("Answer latest question") that:

1. Takes the last **N seconds** of transcript (default 120s, user-configurable) and the **2 most-recent
   screen captures** (labeled with their age).
2. Derives the latest question-in-discussion and its context via a small LLM extraction step.
3. Creates a normal `ConversationTurn` for it and answers it through the existing answer path, so the
   result is a standard card — streamed, 4-part formatted, copyable (`Ctrl+Shift+C`), persisted,
   version-paged.

`Ctrl+Shift+Q` is **unchanged**. This is an additive, separate action.

## Chosen approach — Extract-then-answer (Approach A)

Two stages, maximizing reuse of the existing answer machinery:

1. **Extract** — a new `ILatestQuestionExtractor` (Haiku) reads the transcript window + labeled
   captures and returns the latest question text + a short context summary.
2. **Answer** — create a `Question` + `ConversationTurn` from the derived text, then invoke the
   **existing `GenerateAnswerHandler`** (4-part prompt, token cap, streaming, recent-Q&A context,
   `screenContext`).

Rejected alternatives:
- **One-shot** (single call finds + answers): cheapest but the card's question label is fuzzy and it
  bypasses the structured turn/prompt reuse.
- **Hybrid one-shot** (single structured call returning question + answer): saves one call but forces
  re-implementing parsing/streaming instead of reusing `GenerateAnswerHandler`.

## Components & data flow

### 1. Hotkey & view-model orchestration
- Add `HotkeyId.AnswerLatestQuestion`; bind to `Ctrl+Shift+Z` in `HotkeyDefaults.All`.
- `App.xaml.cs` `WireHotkeys` routes it to a new `ConversationTurnViewModel` command
  (`AnswerLatestQuestionCommand` relay command).
- The VM command reads the recent-captures ring buffer (below), computes each capture's age, and sends
  the mediator command:
  `AnswerLatestQuestionCommand(SessionId, IReadOnlyList<RecentCapture> RecentCaptures)`
  where `RecentCapture = (string AgeLabel, string Ocr)`.

### 2. Recent-captures ring buffer (new, App layer)
Today only the *current* capture group survives in `ScreenCaptureAccumulator` (it clears across
groups), so there is no cross-group history to read "the last 2 captures" from. Add a small ring
buffer in `ConversationTurnViewModel` holding the last 2 `(ocr, timestamp)` entries, appended on each
`CaptureScreenAsync`. The Z command reads the last ≤2 and labels them by age
(e.g. `"captured 35s ago"`).

### 3. Question extraction (new port + Infrastructure impl)
- `ILatestQuestionExtractor` in `Application/Abstractions`.
  - Input: ordered transcript window items (speaker + text), labeled recent captures.
  - Output: `LatestQuestionResult { bool Found, string QuestionText, string ContextSummary }`.
- Infrastructure implementation: a Haiku HTTP call following the **exact proven pattern** of
  `QuestionBoundaryClassifier` / `IScreenFollowUpClassifier` — fenced JSON prompt, strips the
  ```` ```json ```` fence before parsing, returns `Found=false` on parse failure / no question.
- **Prompt-injection (security rule):** transcript lines and OCR are untrusted. They are placed in
  clearly fenced, data-labeled sections (`[Transcript]`, `On-screen context (OCR):`), never blurred
  into the instruction section. Captures are labeled with age and the model is instructed to ignore
  them if unrelated ("let the model decide" capture policy).

### 4. Orchestrating handler (`AnswerLatestQuestionHandler`, Application layer)
1. Load session via `ISessionRepository`.
2. Filter `session.Transcript` to items with `Timestamp >= now - window` (window from settings).
3. If the window is empty → surface "No recent question found." via the answer error sink; stop.
4. Call `ILatestQuestionExtractor`.
   - `Found == false` → surface "No recent question found."; no turn created.
   - `Found == true` → create a new `Question` + `ConversationTurn` from `QuestionText` (reuse the
     same domain path the pipeline uses to create question turns), announce via
     `ITurnSink.OnTurnCreated(turnId, questionText)`, persist, then send the existing
     `GenerateAnswerCommand(SessionId, turnId, Preliminary, screenContext: labeledCaptures)`.
5. The answer streams to the new card through the existing `IAnswerStreamSink` path.

**Concurrency:** if a Z-generation is already in flight, ignore further Z presses (v1 simplicity).

### 5. Settings slider
- `AppSettingsDto` gains `int LatestQuestionWindowSeconds` — **default 120**, range **30–300**.
- Mirror the `MaxAnswerTokens` plumbing exactly: DTO default + range constants, `SaveSettings`
  validation/clamp, `SettingsViewModel` observable property, `SettingsWindow.xaml` slider + value
  label, read live in `AnswerLatestQuestionHandler`.
- Settings are persisted as **settings JSON** (not EF) → **no migration**.

## Error handling
- **No active session** → no-op (consistent with other hotkeys).
- **Empty window / extractor `Found=false`** → friendly "No recent question found." surfaced as an
  error-style notice; no turn, no persisted card.
- **API failure during extraction or answering** → existing friendly-error path
  (`AnswerErrorMessage.ForUser`); no partial card left in a broken state.
- **Captures absent** → omit the OCR section; answer from transcript alone.

## Reuse / no new persistence
- Reuses existing entities (`Question`, `ConversationTurn`, `AnswerVersion`) and the existing
  `GenerateAnswerHandler`, `IAnswerStreamSink`, `ITurnSink`, `AnswerErrorMessage`.
- No EF entity change ⇒ **no migration**. Settings change is JSON only.

## Testing
- **Application:** `AnswerLatestQuestionHandler` with a fake `ILatestQuestionExtractor` —
  found / not-found / empty-window paths; window filtering; capture age-labeling; verifies turn
  creation, `OnTurnCreated`, and `GenerateAnswer` invocation. `AppSettingsDto` range + JSON
  round-trip for `LatestQuestionWindowSeconds`.
- **Infrastructure:** extractor fence-strip / JSON-parse tests (parallel to
  `QuestionBoundaryClassifierTests`), including the `Found=false` fallback on malformed output.
- **App:** ring-buffer keeps only the last 2 captures; `SettingsViewModel` slider clamp/persist.
- **Integration (E2E):** seed a transcript containing a "missed" question, fire the handler, assert a
  card appears via `ITurnSink.OnTurnCreated` with a streamed answer.
- **Optional opt-in:** a gated Haiku eval for extractor quality (reuse the `run-ai-eval` skill);
  informational only, never a CI gate (non-deterministic).

## Out of scope (YAGNI)
- Making the capture count configurable (fixed at 2).
- A dedicated `AnswerVersionType` for this path (reuse `Preliminary`).
- Queuing/cancel-and-restart concurrency (v1 ignores Z while one is in flight).
