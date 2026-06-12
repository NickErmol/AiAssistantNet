# ASR-Degradation Hardening + Eval-as-Guard

**Date:** 2026-06-12
**Branch:** `feature/asr-degradation-hardening`
**Status:** Design — awaiting plan
**Predecessors:** Spec 3a (guardrails + JSONL decision recorder, PR #21), Spec 3b (eval
harness + baseline, PR #22), Spec 3d (corpus expansion + prompt tuning, PR #37)

## Problem

Spec 3d tuned the boundary classifier to held-out 12/12 against *clean* transcripts. A live
overlay run (2026-06-11) exposed a failure mode the corpus does not cover: **garbled ASR**.

The interviewer asked *"Walk me through how you'd add caching without serving stale data"* —
a legitimate new question that should spawn its own card. Whisper mistranscribed it as
*"Welcome through how you would add cash in without service day of data"* ("Walk me"→"Welcome",
"caching"→"cash in", "stale data"→"service day of data"). The boundary log
(`boundary-decisions-2026-06-11.jsonl`, 14:28:16) shows: heuristic `NoQuestion` 0.3 → AI called
(heuristic < 0.7) → **AI `QuestionContinued` 0.72** → routed to `AppendContinuation` → folded
into the prior "writes spike" turn. **No new card.**

This did *two* harmful things at once:
1. **No card** for a real question (the interviewee never sees that a question was asked).
2. The garbled text was **folded into the prior turn**, corrupting an otherwise-good question
   and triggering a regeneration of that turn against contaminated input.

The 0.72 confidence is why the existing Spec 1/2 safety-net re-check did not fire — it only
engages when `result.Confidence < 0.7`. Whisper is **confidently wrong** on garbled audio, so
no text-derived confidence threshold catches this. The one signal that *is* independent of the
(wrong) words is the **ASR confidence** Whisper emits per segment — and the boundary router
**never consults it today** (the routing decision is 100% text-derived).

## Goal

Ship a **measured, regression-guarded** improvement that hardens the classifier against ASR
degradation and turns the eval into a real quality bar, released as a new version.

1. **Close the ASR-degradation gap (the floor).** Stop low-ASR-confidence interviewer speech
   from being silently folded into a good existing turn (harm #2). Deterministic, CI-guarded.
2. **Add garbled corpus coverage** so the classifier's text-side behavior on real manglings is
   measured, and **harden generalization confidence** by growing the held-out set.
3. **Make the eval a real guard** — assert a model-quality floor when an API key is present, and
   hard-guard the new deterministic gate logic in CI.
4. Cut a **new release version** for the change.

**Success criteria:**
- A low-ASR-confidence interviewer item that would fold into a live turn is **dropped** (turn
  unchanged), recorded with an `AsrDropped` route — proven by a CI pipeline test.
- Garbled corpus cases exist and are evaluated against Haiku to **document** how the text-only
  classifier (mis)handles degraded input. *(Updated after the 2026-06-12 run — see Results: this
  is report-only, not an accuracy/fold guard, because there is no reliable text-only "correct"
  label for garbled input. The classifier's unreliability here is what justifies the gate.)*
- Held-out corpus grown to ~24 with a re-recorded Haiku baseline.
- AI eval asserts a minimum accuracy on the **held-out** set when a key is present.

## Non-goals / out of scope

- **No "possible missed question" overlay cue** (option (ii) from brainstorming). The honest-UX
  path needs new overlay state — a future follow-up. This spec accepts the *miss* (harm #1) and
  only fixes the *silent corruption* (harm #2).
- **No suppression of a garbled `NewQuestion` that spawns a fresh card.** That is visible (not
  silent) noise, and suppressing it risks dropping real questions. The demonstrated harm is the
  fold; that is the scope.
- **No user-facing setting for the threshold.** It stays a tunable `const` (no EF/settings/
  migration scope). The threshold is observable for field-tuning via the decision log.
- **No `BoundarySplitGuard` / Domain / EF / migration / model change.** Same labels, same JSON
  contract, same `claude-haiku-4-5-20251001`.
- **No automated AI-in-CI.** The real-Haiku eval stays opt-in/env-gated; keyless CI skips it.

## Design

### Component A — ASR-confidence fold-guard (the floor)

A new pure helper `AsrConfidenceGate` in `src/AIHelperNET.Application/Sessions/`, sibling to
`BoundarySplitGuard`. Single decision method:

> Given `(double asrConfidence, BoundaryLabel resolvedLabel, bool liveTurnExists)` → return
> whether to **suppress a fold-into-an-existing-turn**.

It returns *suppress* only when **all** hold:
- `asrConfidence < AsrFloor` — a named `const double` (initial **0.45**, tunable), documented as
  field-tunable from `boundary-decisions-*.jsonl`.
- `resolvedLabel` is a **fold/append** label: `QuestionContinued`, `AdditionalRequirement`,
  interviewer (`Speaker.Other`) `ClarificationOfCurrentQuestion`, **or** a `NewQuestion` the
  `BoundarySplitGuard` already demoted to append-to-active-turn.
- `liveTurnExists` — there is a non-terminal active turn that *would be* corrupted.

**Wiring** (`TranscriptPipelineService.BuildCommandWithBoundaryAsync`): after `result` and the
split-guard decision are computed, before `RouteLabel`. When the gate suppresses, the router is
bypassed (no command, no append — equivalent to a `NoQuestion`/no-op) and the decision is
recorded with route `AsrDropped`. The `NewQuestion→append` case is gated by passing the
guard-demoted label into the gate so a low-confidence demoted split is dropped rather than
appended.

The gate is **deterministic** — it reads `item.Confidence`, which the classifier never sees.
No API call, fully unit-testable.

**Observability:** add `double AsrConfidence` to `BoundaryDecisionRecord` (and the
`BoundaryRoute` structured log line) so the floor can be tuned from real diagnostics. This is a
metadata field (a float), not PII — consistent with the metadata-only diagnostics rule.

### Component B — garbled corpus + held-out expansion

**Garbled cases** — a new `tests/AIHelperNET.Integration.Tests/Eval/boundary-garbled.json`
(~6–8 entries), same `CorpusEntry` shape as the existing corpora (text + expected label; no
schema change). Built from the real field mistranscription plus realistic synthetic Whisper
manglings (homophone swaps, dropped words, verb corruption) across varied topics. Expected
labels reflect what Haiku *should* do with garbage — `Unrelated` / `NoQuestion`. The property
under test: garbled input must **not** be confidently classified as a fold label
(`QuestionContinued`/`AdditionalRequirement`), which is the text-side mirror of the harm
Component A guards structurally. Evaluated by a new opt-in
`RealHaiku_OverGarbled_ProducesReport` test mirroring the existing two.

> Note on the two surfaces: the garbled corpus exercises the **classifier** (text-only — it
> never sees ASR confidence). The fold-guard is a **pipeline** concern (uses ASR confidence) and
> is tested separately in pipeline tests. They are complementary, not redundant: A stops the
> structural fold even when Haiku is confidently wrong; B keeps the text-side from *being*
> confidently wrong where possible.

**Held-out expansion** — grow `boundary-holdout.json` from 12 → ~24 entries with novel topics/
phrasings across all labels (so "100% held-out" carries weight), then re-run the opt-in Haiku
eval and record the new baseline in the Results section.

### Component C — eval-as-guard

Today `BoundaryClassifierAiEvalTests` *writes a report and asserts nothing*; it skips silently
without a key. Changes:

1. **Assert a model-quality floor when a key is present.** After building each report, assert a
   minimum accuracy on the **held-out** set: **≥ 0.90**. The **garbled** set is **report-only**
   (no assertion) — the 2026-06-12 run showed garbled input has no reliable text-only gold label,
   so an accuracy/fold floor there would assert something untrue (and the classifier *does*
   occasionally emit a fold label on garbage — the very reason the gate exists). When
   `AIHELPER_AI_EVAL_KEY` is unset the test still returns early (keyless CI unchanged). So a
   held-out prompt regression *fails the test* wherever a key exists (local / nightly).
2. **Hard-guard the deterministic pieces in CI** (always run, no key):
   - `AsrConfidenceGateTests` — unit tests around the threshold and each fold label, plus the
     non-fold labels (`NewQuestion`-fresh, `QuestionComplete`, etc.) that must **not** be gated.
   - A `TranscriptPipelineServiceTests` case: a live answered turn + a low-confidence
     interviewer continuation → assert the turn's question is **unchanged**, no
     `GenerateAnswerCommand` is produced, and the decision is recorded with route `AsrDropped`.
     A companion high-confidence case asserts the same input *does* fold (no false positives).

### Component D — release version

The repo currently tracks **no version anywhere** (`Directory.Build.props` has no `<Version>`,
csprojs have none). "New release version" therefore means *introducing* versioning. Plan:

- Add `<Version>` (and `<AssemblyVersion>`/`<FileVersion>`) to `Directory.Build.props` so all
  assemblies stamp a single source-of-truth version. Propose **`0.0.0` → `0.1.0`** (first
  tracked version; the app has been pre-1.0 untracked). **Confirm the target number with the
  user** before stamping.
- Per gitflow: `feature/asr-degradation-hardening` → PR → `develop`, then cut `release/0.1.0`,
  tag `v0.1.0`. (The actual release/tag steps are an executor action gated on user confirmation
  of the number.)

## Components & boundaries

| Unit | Change | Depends on | Testable by |
|------|--------|-----------|-------------|
| `AsrConfidenceGate` (new) | pure decision helper | nothing | `AsrConfidenceGateTests` (CI) |
| `TranscriptPipelineService` | call gate before `RouteLabel`; record `AsrDropped` | `AsrConfidenceGate` | `TranscriptPipelineServiceTests` (CI) |
| `BoundaryDecisionRecord` + log | add `AsrConfidence` field + `AsrDropped` route | nothing | recorder tests (CI) |
| `boundary-garbled.json` (new) | ~6–8 garbled cases | nothing (data) | `RealHaiku_OverGarbled` (opt-in) |
| `boundary-holdout.json` | 12 → ~24 entries | nothing (data) | `RealHaiku_OverHoldout` (opt-in) |
| `BoundaryClassifierAiEvalTests` | accuracy assertions when key present | corpora | itself (opt-in) |
| `Directory.Build.props` | introduce `<Version>` | nothing | build |

No interface/label/JSON-contract change. The classifier's behavioral contract is unchanged;
only the pipeline gains a deterministic pre-routing gate.

## Risks & mitigations

- **Whisper confidence is itself unreliable (confidently wrong).** The gate only *suppresses
  folds* below the floor — a missed genuine continuation merely fails to refine an existing
  answer (low harm), whereas the harm avoided (corrupting a good turn / regenerating on garbage)
  is higher. The floor starts conservative (0.45) and is field-tunable via the new log field.
- **Threshold too aggressive → drops real continuations.** Mitigation: start low, log
  `AsrConfidence` on every decision, and the companion high-confidence pipeline test guards
  against over-broad gating. The exact value is explicitly a tuning target, not a guess locked
  forever.
- **Garbled expected-labels are subjective.** Mitigation: assert the *negative* property (not a
  fold label) more strongly than the exact `Unrelated` vs `NoQuestion` choice; garbled floor is
  0.80, not 1.0, to absorb fuzziness.
- **Versioning introduction touches every assembly.** Mitigation: a single `Directory.Build.props`
  property; build-verified; number confirmed with the user before stamping.

## Acceptance

1. `AsrConfidenceGate` exists; low-confidence fold labels with a live turn are suppressed,
   non-fold labels and high-confidence inputs are not — proven by `AsrConfidenceGateTests`.
2. Pipeline test: low-confidence interviewer continuation on a live answered turn → turn
   unchanged, no command, `AsrDropped` recorded; high-confidence companion still folds.
3. `BoundaryDecisionRecord`/log carry `AsrConfidence`; `AsrDropped` route emitted.
4. `boundary-garbled.json` (~6–8) and expanded `boundary-holdout.json` (~24) checked in;
   re-recorded Haiku baseline in Results.
5. AI eval asserts held-out ≥ 0.90 when a key is present; garbled set is report-only
   (documents classifier behavior on degraded input); keyless CI skips.
6. `<Version>` introduced; `dotnet build` / `dotnet test` green; version number user-confirmed.
7. No Domain / EF / migration / `BoundarySplitGuard` / model change.

## Results (2026-06-12)

**Shipped `AsrFloor`:** 0.45 (Whisper segment probability). Recorded on every boundary decision
(`AsrConfidence` field + `asr=` log) for field-tuning.

**Released version:** `0.1.0` (first tracked version; introduced in `Directory.Build.props`).

**Deterministic suite (CI, no key):** Domain 104 · Application 213 (incl. 6 `AsrConfidenceGate` +
2 pipeline drop/fold tests) · Infrastructure 44 · Integration-Eval 12 — all green.

**Real-Haiku eval** (`claude-haiku-4-5-20251001`, opt-in via `AIHELPER_AI_EVAL_KEY`):

| Set | Size | Accuracy | Guard |
|---|---|---|---|
| Tuning corpus | 55 | **100.0%** | report-only |
| **Held-out (generalization)** | 24 | **100.0%** | **≥ 0.90 — PASS** |
| Garbled (ASR degradation) | 7 | 42.9% (3/7) | **report-only** |

The held-out 100% on the expanded 24-case novel set confirms the Spec 3d prompt tuning generalizes.

**Key finding — the garbled set re-scoped the eval-as-guard.** The garbled run disproved the spec's
initial assumption that degraded text reliably labels `Unrelated` and that the classifier would
never emit a fold label on garbage:

- Haiku **recovers intent from some garble**: `g-schema-garble` ("sketch out the scheme are for a
  movie booking sister") → `TaskComplete` — semantically *correct* despite the mangling. So
  "Unrelated" was the wrong gold label; there is no reliable text-only label for garbled input.
- Haiku **occasionally emits a fold label anyway**: `g-rate-limit` → `QuestionContinued` — with no
  signal that the transcript was garbage. This is the exact failure mode the deterministic
  `AsrConfidenceGate` exists to catch (it reads ASR confidence, not the words).

Conclusion: the garbled corpus is **documentation of why the gate is needed**, not a model-quality
guard. The accuracy/fold assertions on it were removed (made report-only); the held-out ≥ 0.90
guard and the deterministic gate/pipeline tests are the real regression guards. This re-scoping was
discovered by running the eval before merge — the intended value of eval-first sequencing.

Garbled misses (for the record): `g-process-thread`→`QuestionComplete`,
`g-index-tradeoff`→`NoQuestion`, `g-rate-limit`→`QuestionContinued`, `g-schema-garble`→`TaskComplete`.

**Follow-ups noted (out of scope, from the final review):**
- `SessionRunner` same-speaker segment merge keeps the *first* fragment's confidence; a clean
  lead-in + garbled tail could carry high confidence past the gate. A `min-of-merged` confidence
  would be a more faithful signal — candidate follow-up.
- Option (ii) "possible missed question" overlay cue still parked (harm #1, the missing card, is
  the accepted non-goal — only the silent corruption / harm #2 is fixed).
