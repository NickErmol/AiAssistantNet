# Spec 3a — Boundary-classifier guardrails + decision observability

**Date:** 2026-06-09
**Branch:** `feature/boundary-guardrails`
**Status:** Design — awaiting plan
**Predecessors:** Spec 1 (conversation core, PR #15), Spec 2 (endpointing/continuation segmentation, PR #20)
**Successors:** Spec 3b (eval harness + tuning), Spec 3c (UI best-practices review)

## Problem

Spec 2 deliberately "trusts the classifier label" with no recency guard. When the
boundary classifier returns `NewQuestion` while a turn is live, the pipeline
unconditionally dismisses the active turn and opens a new card
(`TranscriptPipelineService.RouteLabel`, the `NewQuestion` branch:
`activeTurn.Dismiss()` + `HandleNewQuestion`). A single mislabel therefore splits one
question into two half-cards.

The dominant failure is **over-splitting** ("Scenario A"): an interviewer continues a
question a couple of seconds after the first answer fired —

> "Let's talk about caching strategies…" → turn opens, answer fires
> (2s later) "…specifically, how would you handle cache invalidation?"

The continuation reads enough like a fresh question that the classifier returns
`NewQuestion` (~0.8 confidence). The caching card is dismissed and a new, context-poor
card replaces it. One asked question becomes two broken cards.

The reverse, **under-splitting** ("Scenario B" — a genuinely new question folded into the
prior turn), exists but is less common and is bounded by the design below.

There is also almost no observability: a single `BoundaryRoute:` Information log line per
item, with no record of the heuristic-vs-AI inputs, which guard fired, or why.

## Goal

1. Stop a single `NewQuestion` mislabel from splitting a live, recently-active turn —
   using deterministic, unit-testable guards.
2. Durably record every routing decision so it can be inspected and, in Spec 3b, replayed
   and measured.

Both are scoped to be low-risk: the **only** behavioural change is at the destructive
`NewQuestion` branch; every other route is untouched. No schema/migration change.

## Design

### 1. The guard — a pure, isolated component

A new pure class `BoundarySplitGuard` (in `AIHelperNET.Application/Sessions/`), no I/O and
no clock, exposing one decision method:

```csharp
public enum SplitDecision { Split, AppendToActiveTurn }

SplitDecision Evaluate(
    double effectiveConfidence,   // see §3
    bool hasLiveTurn,             // ActiveTurn != null && !IsTerminal
    TimeSpan sinceLastActivity);  // from the recency tracker, §4
```

Decision rule (composes all three approved guards — recency #1, asymmetric confidence #2,
agreement #3 via `effectiveConfidence`):

- `!hasLiveTurn` → **Split** — nothing to protect.
- `hasLiveTurn` and `sinceLastActivity > RecencyWindow` → **Split** — the prior turn is
  stale enough; this is a genuine new question.
- `hasLiveTurn`, recent, and `effectiveConfidence >= SplitConfidenceBar` → **Split** —
  high, agreed confidence clears the destructive bar.
- otherwise → **AppendToActiveTurn**.

Being pure, the guard is tested with a truth table — no pipeline or clock required.

The guard is consulted **only** in the `NewQuestion` branch. `QuestionComplete` /
`TaskComplete` that open a turn when none is collecting are additive (they do not dismiss a
live turn) and are left unchanged.

### 2. Integration point

In `RouteLabel`, the `NewQuestion` case currently does:

```csharp
case BoundaryLabel.NewQuestion:
    if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
        activeTurn.Dismiss();
    return HandleNewQuestion(session, item.Text, item.Timestamp);
```

It becomes: compute `hasLiveTurn` / `sinceLastActivity`, ask `BoundarySplitGuard`. On
`Split`, behave exactly as today (including the `CollectingQuestion` dismiss-then-split
sub-case). On `AppendToActiveTurn`, the suppressed split refines the live turn instead of
replacing it, mirroring the existing `QuestionContinued` handling:

- if the active turn is still `CollectingQuestion` → `activeTurn.AddFragment(item.Text)`
  (the question is still being assembled; no regen yet);
- otherwise → the existing continuation path (`AppendContinuation` → `AppendToQuestion`
  + `ScheduleRegen`).

### 3. Effective confidence (the agreement guard, #3)

Today `BuildCommandWithBoundaryAsync` discards the heuristic result the moment it calls
Haiku, then trusts the AI alone. Change: retain both opinions and derive an
`effectiveConfidence`:

- When both the heuristic and the AI produced an opinion and they **disagree on whether
  this is a split** (one says `NewQuestion`, the other says a continuation-family label —
  `QuestionContinued` / `ClarificationOfCurrentQuestion` / `AdditionalRequirement`), demote
  the confidence below `SplitConfidenceBar` (multiply by `DisagreementPenalty`).
- When only one opinion exists (the heuristic was confident `>= 0.7`, so the AI was never
  called), no demotion — that opinion's own confidence stands.

The computation lives next to the guard call in the pipeline (or a small pure helper) and
is unit-tested independently.

### 4. Recency tracking (via `TimeProvider`)

The pipeline keeps `ConcurrentDictionary<ConversationTurnId, DateTimeOffset>
_lastActivityAt`, stamped with the injected `TimeProvider` (the clock `RegenDebouncer`
already uses) whenever the pipeline touches a turn — create, append, fragment, clarification
attach. The guard reads `_timeProvider.GetUtcNow() - _lastActivityAt[activeTurn.Id]`
(treating a missing entry as "infinitely old" → `Split`).

Entries are removed alongside the existing `_turnCts` cleanup in `DrainStatusFeedback`
(on ready/terminal) and `Dispose`. This dictionary is additional pipeline singleton state
and is noted for the deferred per-session-reset follow-up.

### 5. Observability — recorder port → JSONL

- New port `IBoundaryDecisionRecorder` in `Application/Abstractions`:

  ```csharp
  void Record(BoundaryDecisionRecord record);
  ```

  with `BoundaryDecisionRecord` capturing: timestamp, sessionId, turnId (nullable),
  speaker, staleTurnStatus, heuristicLabel + heuristicConfidence, aiLabel + aiConfidence
  (nullable — AI not always called), agreed (bool), effectiveConfidence, guardApplied
  (the `SplitDecision` / route taken), finalLabel, and a short text clip.

- Infrastructure implementation `JsonlBoundaryDecisionRecorder` writes one JSON line per
  decision to the data root via `AppPaths` — a new `AppPaths.DiagnosticsDir`
  (`<base>/diagnostics`), file `boundary-decisions-{yyyy-MM-dd}.jsonl`. Writes are
  **best-effort**: any I/O exception is caught and logged at Debug; the pipeline never
  fails because recording failed. Append is serialized to avoid interleaved lines.

- The existing `BoundaryRoute:` Serilog line is enriched with the heuristic label/confidence,
  AI label/confidence, agreement, effective confidence, and which guard fired.

- The port is registered in DI as a singleton. It is the corpus source for Spec 3b, which
  can substitute a replay/in-memory recorder behind the same port.

### 6. Constants (named, in code)

Alongside the existing `MaxCollectionSeconds` / `DuplicateThreshold` style:

- `RecencyWindowSeconds = 6`
- `SplitConfidenceBar = 0.90`
- `DisagreementPenalty = 0.5` (demotes a disputed `NewQuestion` below the bar)

Spec 3b calibrates these against recorded data.

## Testing

- **`BoundarySplitGuard`** — truth table over (`effectiveConfidence`, `hasLiveTurn`,
  `sinceLastActivity`) asserting `Split` vs `AppendToActiveTurn` at and around the
  thresholds.
- **Effective-confidence / agreement** — heuristic-only (no demotion); agree (no demotion);
  disagree (demoted below the bar).
- **Recency tracker** — stamping on touch, expiry past the window, missing-entry →
  treated as stale; all driven by a fake `TimeProvider`.
- **Pipeline sequences** — Scenario A (continuation 2s after a fired answer) appends and
  does not split; a high-confidence `NewQuestion`, and a `NewQuestion` after the recency
  window, both still split; no-live-turn `NewQuestion` splits.
- **Recorder** — writes one line per decision; a write failure is swallowed and does not
  propagate.

## Tradeoffs

The guards deliberately **bias toward cohesion**, marginally raising under-split risk
(Scenario B). The recency window and the `0.90` bar bound it: strong new-topic phrasing
("moving on, another question…") typically arrives after the window or scores high enough
to clear the bar. The window and bar are the primary knobs Spec 3b calibrates with real
recorded decisions.

## Out of scope (deferred)

- Eval harness, labeled corpus, confusion matrix, prompt/threshold tuning, regression
  guard → **Spec 3b** (consumes this spec's JSONL records).
- UI best-practices review and answer-card-pattern work → **Spec 3c**.
- Cooldown-after-split guard (#4) → only if 3b shows fragmentation persists.
- Per-session reset of pipeline singleton state (now also `_lastActivityAt`) → existing
  untracked follow-up.
