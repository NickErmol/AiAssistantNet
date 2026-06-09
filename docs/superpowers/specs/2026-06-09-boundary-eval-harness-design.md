# Spec 3b — Boundary-classifier eval harness + baseline

**Date:** 2026-06-09
**Branch:** `feature/boundary-eval-harness`
**Status:** Design — awaiting plan
**Predecessors:** Spec 1 (PR #15), Spec 2 (PR #20), Spec 3a (PR #21 — guardrails + JSONL decision recorder)
**Successors:** Spec 3c (UI best-practices review); a later, explicitly-manual classifier-tuning follow-up

## Problem

Issue D asks us to reduce the boundary-classifier mislabel rate. Before anything can be
tuned, the mislabel rate must be **measurable and regression-guarded**. Spec 3a added a
JSONL decision recorder (the eventual corpus source) but no way to score classification
accuracy against ground truth.

The hard constraint: the AI classifier (`QuestionBoundaryClassifier`) calls real Claude
Haiku — non-deterministic, costs money, needs an API key — so it cannot run in CI. The
deterministic heuristic (`QuestionBoundaryDetector`) can. A single mechanism cannot serve
both "block regressions on every commit" and "tune the real Haiku prompt." Spec 3b builds
the measurement apparatus and splits those jobs cleanly.

## Goal

Ship the eval **apparatus + a baseline**, fully deterministic and reviewable:

1. A hand-authored labeled corpus of classifier inputs + expected labels.
2. A pure confusion-matrix / report core.
3. A CI heuristic-regression test that asserts accuracy stays at/above a measured baseline.
4. An opt-in (non-CI) harness that runs the real Haiku classifier over the same corpus to
   produce a tuning report.

**No prompt/threshold changes and no tuning in this spec** — tuning is a data-driven
follow-up once the AI harness has been run and real numbers observed.

## Design

### 1. Corpus (hand-authored, JSONL-aligned)

Checked-in `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json`. Each entry:

```jsonc
{
  "id": "continuation-after-answer-1",
  "recentItems": [ { "speaker": "Other", "text": "Let's talk about caching strategies." } ],
  "latestItem":  { "speaker": "Other", "text": "how would you handle cache invalidation" },
  "activeTurnStatus": "PreliminaryReady",   // or null
  "expectedLabel": "QuestionContinued",
  "note": "Scenario A — continuation that looks like a new question"
}
```

- `speaker` ∈ `Me | Other`; `activeTurnStatus` is a `ConversationTurnStatus` name or `null`;
  `expectedLabel` is a `BoundaryLabel` name.
- ~25–40 entries spanning every category: `NoQuestion`/`Unrelated` (filler/noise),
  `QuestionStarted` (scenario starters), `QuestionContinued`, `QuestionComplete`,
  `TaskComplete`, `ClarificationOfCurrentQuestion`, `AdditionalRequirement`, `NewQuestion`,
  plus explicit Scenario A (over-split: continuation that reads like a new question) and
  Scenario B (under-split: genuine new question after an answered turn) cases.
- The schema is a **superset** of `BoundaryDecisionRecord`. A future harvest from real
  recorder JSONL can populate `latestItem` / `speaker` / `activeTurnStatus` / labels, but
  not `recentItems` (the record does not capture them) — a documented limitation, not a
  blocker for the hand-authored v1.

A small `CorpusLoader` deserializes the file; malformed JSON or an empty corpus fails the
test run loudly (it is a setup error, not a silent pass).

### 2. Pure eval core

In `tests/AIHelperNET.Integration.Tests/Eval/`, no I/O:

- `ConfusionMatrix` — `Record(BoundaryLabel expected, BoundaryLabel predicted)`,
  `Accuracy`, `PrecisionFor(label)`, `RecallFor(label)`, `Total`.
- `EvalReport` — wraps a `ConfusionMatrix` and renders `ToText()` (the matrix grid +
  overall accuracy + per-label precision/recall). This text is the observability
  deliverable: it shows exactly which labels are confused for which.

Both are pure and unit-tested with a tiny synthetic label set (no corpus, no classifier).

### 3. Heuristic regression test (CI, deterministic)

`BoundaryHeuristicEvalTests`:

1. Load the corpus.
2. For each entry, call `QuestionBoundaryDetector.Evaluate(latestItem.text,
   latestItem.speaker, activeTurnStatus, recentQuestions)`, where `recentQuestions` is
   derived from the `Other`-speaker texts in `recentItems` (matching how the pipeline feeds
   the detector).
3. Record (expected, predicted-classification) into a `ConfusionMatrix`.
4. Write `EvalReport.ToText()` to xUnit test output.
5. Assert `matrix.Accuracy >= Baseline`.

Deterministic, no network — runs on every CI build as the regression guard.

### 4. Opt-in real-Haiku harness (NOT in CI)

`BoundaryClassifierAiEvalTests`, gated so CI skips it: runs only when an env var
(`AIHELPER_AI_EVAL=1`) is set **and** an API key is configured; otherwise it skips/returns
early with a clear message. When enabled it:

1. Builds the real `QuestionBoundaryClassifier` (real `HttpClient` + secret store + options).
2. For each corpus entry, calls `ClassifyAsync(activeTurnStatus, recentItems, latestItem,
   speaker, ct)` (mapping `recentItems`/`latestItem` to `TranscriptItem`s).
3. Records results into a `ConfusionMatrix`; per-call API failures are counted in a separate
   bucket and never crash the run.
4. Writes `EvalReport.ToText()` plus the list of mismatched cases to a report file under the
   data root (`AppPaths` diagnostics dir) and to test output.

This is the tuning instrument used by the deferred tuning follow-up. It makes **no**
assertion about accuracy (the real model's score is informational), so it is never a flaky
gate.

### 5. Baseline

`Baseline` is set during implementation by measurement: run the heuristic eval, observe the
actual accuracy X over the authored corpus, and set `Baseline = X` (locking current
behavior so a future detector change that degrades accuracy fails CI). The measured number
is recorded in the implementing commit. The corpus is authored first and labeled by human
judgement; the baseline reflects the heuristic's accuracy against those labels, not a
target.

### 6. Placement & isolation

All new code under `tests/AIHelperNET.Integration.Tests/Eval/` (that project already
references Infrastructure, so it can drive both the pure heuristic and the real classifier).
Reusable units are the corpus model + loader and `ConfusionMatrix`/`EvalReport`; the two
test classes are thin drivers over them.

## Testing

- `ConfusionMatrix` / `EvalReport` — unit tests over a small synthetic label set
  (accuracy, precision, recall, text rendering).
- `CorpusLoader` — loads the checked-in corpus, asserts it is non-empty and every
  `expectedLabel` parses to a `BoundaryLabel`.
- `BoundaryHeuristicEvalTests` — the regression test itself (accuracy ≥ baseline).
- The opt-in AI harness is verified to **skip** cleanly when the env var/key are absent
  (so CI proves the gate works without calling Haiku).

## Tradeoffs

- The corpus is hand-authored, so it reflects authored judgement rather than a real-traffic
  distribution. That is the correct v1 choice (no real sessions recorded yet) and guarantees
  edge-case coverage a random sample would not; the schema is harvest-ready for later.
- The baseline locks the heuristic's *current* accuracy, not an aspirational target — its
  job is regression detection, not goal-setting.

## Out of scope (deferred)

- Any Haiku prompt change, threshold change, or tuning — a later manual follow-up using the
  opt-in harness.
- Activating the dormant agreement guard (Spec 3a finding).
- The JSONL→corpus harvest converter (build when real recorded sessions exist).
- Spec 3c (UI best-practices review).
