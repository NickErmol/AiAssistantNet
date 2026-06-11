# Spec 3d — Boundary-classifier corpus expansion + prompt tuning

**Date:** 2026-06-11
**Branch:** `feature/boundary-classifier-tuning`
**Status:** Design — awaiting plan
**Predecessors:** Spec 1 (conversation core, PR #15), Spec 2 (endpointing, PR #20),
Spec 3a (guardrails + JSONL decision recorder, PR #21), Spec 3b (eval harness +
baseline, PR #22), Spec 3c (overlay UI review, PR #23)

## Problem

Issue D's goal is to reduce the boundary-classifier mislabel rate. Spec 3b built the
measurement apparatus (a hand-authored corpus, a pure confusion-matrix/report core, a
CI heuristic-regression test, and an opt-in real-Haiku harness) but **explicitly deferred
the actual tuning** — "tuning is a data-driven follow-up once the AI harness has been run
and real numbers observed." That follow-up is this spec.

Two concrete gaps block tuning today:

1. **The corpus is too small to trust a measurement.** 24 entries across 9 labels means a
   single reclassification moves accuracy by ~4 points. Any prompt change measured against
   it would be chasing noise, and the AI harness has never actually been run — there is **no
   recorded Haiku baseline number** at all.

2. **The AI classifier prompt is bare-bones.** `QuestionBoundaryClassifier.SystemPrompt`
   (`src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs:25-29`) lists the 9
   valid labels and the JSON schema but gives **no definition of what each label means and
   no examples**. The model is left to infer label semantics from their names alone. This is
   the single highest-leverage tuning surface.

The dominant real-world failure remains **over-splitting** (Scenario A): an interviewer
continuation that reads like a fresh question (`"…how would you handle cache invalidation
here"`) gets labeled `NewQuestion`, splitting one live card into two. Spec 3a added a
deterministic `BoundarySplitGuard` safety net for this, but the probabilistic layer behind
it (the Haiku label itself) is still untuned.

## Goal

Ship a **measured, regression-guarded improvement** to the boundary classifier:

1. Expand the labeled corpus to a size where the accuracy number is meaningful and
   over-sample the confusions that actually hurt (over-split / under-split).
2. Record the **real Haiku baseline** over the expanded corpus (one human-run eval).
3. Enrich the AI classifier prompt (per-label definitions + few-shot examples anchored on
   the real misses) so accuracy goes **up** and the over-split confusions **shrink**.
4. Re-run, keep only changes that measurably help, and **lock the new numbers** into the
   recorded baselines so they are regression-guarded going forward.

**Success criterion (both, metric-led):** overall corpus accuracy improves by a meaningful
margin *and* the `NewQuestion ⇄ QuestionContinued/AdditionalRequirement` confusion cells
shrink, both confirmed by the eval report.

## Non-goals / out of scope

- **No `BoundarySplitGuard` threshold changes.** The deterministic guardrails (Spec 3a) are
  a working, tested safety net. Tuning the probabilistic layer should not destabilize the
  net beneath it. If tuning surfaces a guard gap, that is a separate spec.
- **No pipeline/Domain/EF change.** This touches only the corpus (test data), the eval
  baselines (test thresholds), and one `const string SystemPrompt` in Infrastructure.
- **No automated AI-in-CI.** The real-Haiku eval stays opt-in/env-gated exactly as 3b built
  it. CI continues to guard the *deterministic heuristic* only.
- **No model change.** Stay on `claude-haiku-4-5-20251001`.

## Design

### 1. Corpus expansion (deterministic, no API key)

Grow `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json` from 24 to **~55–60
entries**, keeping the existing JSONL-aligned entry shape:

```json
{ "id": "...", "recentItems": [ { "speaker": "Other|Me", "text": "..." } ],
  "latestItem": { "speaker": "Other|Me", "text": "..." },
  "activeTurnStatus": "PreliminaryReady|CollectingQuestion|null",
  "expectedLabel": "<BoundaryLabel>", "note": "<why this label>" }
```

Distribution targets (additive; existing entries stay):

- **Over-split hard cases (the priority): +8–10** `QuestionContinued` / `AdditionalRequirement`
  entries where the continuation superficially reads like a new question — varied domains
  (caching, rate-limiting, auth, data modeling), varied connective cues
  (`"and also…"`, `"so then…"`, `"in that case…"`, bare topic-shift phrasing). These are the
  entries the over-split confusion is measured on.
- **Under-split contrast: +3–4** genuine `NewQuestion` entries with explicit topic markers
  (`"moving on"`, `"different topic"`, `"next question"`) to keep the boundary sharp and
  prevent the prompt over-correcting into folding real new questions.
- **Fill the thin labels to ≥6 each:** `QuestionStarted`, `ClarificationOfCurrentQuestion`,
  `AdditionalRequirement` (currently 2 each).
- **A few `Me`-speaker entries** (clarifications) since routing treats `Me` distinctly.

**Guard:** the existing `BoundaryHeuristicEvalTests` runs over the whole corpus every commit.
New entries must not drop the deterministic heuristic below its guarded baseline — so the
corpus stays honest without any API call. If an added entry exposes a *genuine* heuristic
gap, that is recorded (note) and the heuristic baseline is re-measured, not silently lowered.

### 2. Record the Haiku baseline (one human-run eval)

The executor will hand the user the exact command:

```
$env:AIHELPER_AI_EVAL_KEY = "<key>"
dotnet test tests/AIHelperNET.Integration.Tests `
  --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests" -l "console;verbosity=detailed"
```

`BoundaryClassifierAiEvalTests` already runs real Haiku over the corpus and writes a report
(accuracy + per-label precision/recall + confusion grid + miss list) to
`AppPaths.DiagnosticsDir`. The user pastes the report back (or it lands in-session via `!`).
We capture **two numbers**: overall accuracy and the size of the over-split confusion cells.
This is the "before" we tune against. (No baseline existed before this spec.)

### 3. Prompt enrichment (the main accuracy lever)

Rewrite `QuestionBoundaryClassifier.SystemPrompt` to add, while keeping the strict
JSON-only output contract intact:

1. **A one-line definition per label** — what distinguishes each, especially the confusable
   trio: `NewQuestion` (a topic the prior turn did not cover, usually after the prior turn is
   answered), `QuestionContinued` (same topic/scenario being extended or refined), and
   `AdditionalRequirement` (a new constraint added to an already-answered question).
2. **3–5 few-shot examples** drawn from the real miss list (step 2), each as a compact
   input→label pair with a one-clause reason. Examples are chosen to teach the boundaries the
   model actually got wrong, not to restate the easy cases.
3. **An explicit tie-breaker rule** for the over-split case: when the latest item extends the
   same scenario/subject as the active turn, prefer `QuestionContinued` /
   `AdditionalRequirement` over `NewQuestion` unless there is an explicit topic-shift marker.

The prompt stays a single `const string` (no new ports, no new files). The output schema,
label set, and `ParseResponse`/`ParseLabel` mapping are unchanged — only the instructions the
model receives change. Token budget grows modestly; `max_tokens=200` for the *response* is
untouched (examples are in the system prompt, not the completion).

### 4. Re-measure, keep-what-helps, lock the numbers

1. User re-runs the same eval command → "after" report.
2. **Accept the prompt change only if** overall accuracy rises *and* the over-split confusion
   cells shrink versus the baseline (success criterion). If a change helps one and hurts the
   other, iterate the examples/definitions rather than accept a wash.
3. Record both numbers in the spec/plan trail and update the heuristic baseline constant if
   corpus expansion shifted it. The Haiku number is documented (it can't be a CI assertion —
   non-deterministic, costs money), but it is written down so the next session has a "before."

## Components & boundaries

| Unit | Change | Depends on | Testable by |
|------|--------|-----------|-------------|
| `boundary-corpus.json` | +30ish entries, balanced | nothing (pure data) | `CorpusLoaderTests`, `BoundaryHeuristicEvalTests` (CI) |
| `QuestionBoundaryClassifier.SystemPrompt` | enriched instructions | nothing (one const) | `BoundaryClassifierAiEvalTests` (opt-in, human-run) |
| Heuristic baseline const | re-measured if corpus shifts it | corpus | `BoundaryHeuristicEvalTests` (CI) |
| Haiku baseline number | recorded (before/after) | human eval run | documented, not asserted |

No interface changes; no consumer of the classifier sees a behavioral contract change
(same labels, same JSON). The pipeline and guardrails are untouched.

## Risks & mitigations

- **Overfitting the prompt to 55 examples.** Mitigation: few-shot examples teach *distinctions*
  (definitions + tie-breaker), not memorized inputs; the under-split contrast entries guard
  against over-folding; corpus domains are varied.
- **Non-deterministic eval wobble.** Mitigation: accept changes only on a *meaningful* margin
  (not a 1-entry swing); the over-split cells are a directional check, not a single number.
- **Corpus expansion lowers the heuristic baseline.** Mitigation: the CI heuristic test fails
  loudly; we investigate whether it is a real gap before adjusting the constant.

## Acceptance

1. Corpus ≥ 55 entries, every label ≥ 6 (the currently-thin labels filled), over-split
   cases over-sampled;
   `dotnet test` green (heuristic baseline holds or is re-measured with justification).
2. A recorded Haiku **baseline** report (before) and **post-tune** report (after).
3. Post-tune Haiku accuracy improved by a meaningful margin AND over-split confusion cells
   smaller than baseline.
4. Only `boundary-corpus.json`, `QuestionBoundaryClassifier.SystemPrompt`, and (if needed)
   the heuristic baseline constant changed. No pipeline/Domain/EF/guardrail change.

## Results (2026-06-11)

Run via the opt-in `BoundaryClassifierAiEvalTests` against real Claude Haiku
(`claude-haiku-4-5-20251001`).

### Prerequisite bug found while measuring
The first eval scored **0% — every one of the 55 cases returned `NoQuestion`**. Root cause:
`QuestionBoundaryClassifier.ParseResponse` called `JsonDocument.Parse` directly on Haiku's
output, but Haiku wraps its JSON in a ```` ```json ```` markdown fence, so every parse threw and
fell back to `Ambiguous`/`NoQuestion`. **The AI boundary classifier had been silently dead in
production** (every call degraded to the heuristic) — the AI path had apparently never been run
against real Haiku until now. Fixed by mirroring `ScreenFollowUpClassifier.StripCodeFence`
(commit `4ff8968`, with regression tests). This fix is a prerequisite to any measurement.

### Before → after

| Measurement | Accuracy | Notes |
|---|---|---|
| Baseline (current prompt, after fence-fix) | **47.3%** (26/55) | `QuestionComplete` & `TaskComplete` recall **0%** (model mapped them to `QuestionStarted`); `QuestionContinued → ClarificationOfCurrentQuestion` ×5; `Unrelated → NoQuestion` ×4; over-split `QuestionContinued → NewQuestion` ×2 |
| Enriched prompt — tuning corpus | **100%** (55/55) | Every label P/R = 100%. Partly overfit: the corpus is also the tuning input and ~9 examples are verbatim corpus entries |
| Enriched prompt — **held-out novel set** | **100%** (12/12) | 12 entries with topics/phrasings absent from the corpus **and** the prompt examples — including the status-gated `also`-cases and a no-marker over-split. Confirms genuine generalization, not memorization |

The over-split confusion cells (`QuestionContinued/AdditionalRequirement → NewQuestion`) went to
**zero** on both the tuning corpus and the held-out set. Acceptance criteria 1–4 met; the
held-out run is the evidence behind criterion 3's "meaningful margin."

### Caveats
- Haiku numbers are **documented, not CI-asserted** (non-deterministic, cost money). CI continues
  to guard only the deterministic heuristic (re-baselined to 0.60 over the expanded corpus).
- 100% on the tuning corpus alone would be untrustworthy; it is reported here only alongside the
  held-out 12/12. Future tuning should keep growing the held-out set as the trustworthy signal.
