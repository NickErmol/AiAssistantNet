# Screen-Answer-Card Live Eval — Design

**Date:** 2026-06-13
**Branch:** TBD (new branch off `develop` after PR #44 merges)
**Status:** Draft — awaiting user review

## Motivation

`InterviewScenarioTests` (PR #44) pins the deterministic pipeline up to the model call — mode
classification and prompt assembly — but deliberately stops there: with no LLM it cannot judge
whether the generated **answer card** is actually correct. `Test 1.docx` asks the real question:
*"What will be in the response card?"*

This eval answers that question against the **live model**, opt-in, the same way the boundary
classifier eval (`BoundaryClassifierAiEvalTests`) and Spec B harness (`SpecBAnswerDepthLiveTests`)
already do: self-skip (pass trivially) with no API key so CI stays green; run for real when a key is
present. It builds the **production** screen-mode prompt, generates the card with the production
model, and scores it.

## Scope

For each scenario (the `Test 1.docx` cases plus the .NET/Angular set):

1. Build the prompt with the real `PromptBuilderService.BuildWithScreenMode` (and
   `BuildScreenFollowUp` for the follow-up turn) — the exact production prompt.
2. Generate the answer card by calling the real Anthropic API with the production model
   (`ClaudeOptions.Model`, default `claude-opus-4-8`), non-streaming so `stop_reason` /
   `usage.output_tokens` are readable (the Spec B technique).
3. **Score in two tiers:**
   - **Deterministic gates (enforced where a key exists):** the card is not truncated
     (`stop_reason != "max_tokens"`); for coding scenarios it contains a fenced code block of the
     expected language; any `requiredSubstrings` are present (e.g. the distinct-values follow-up
     must contain `DISTINCT`).
   - **LLM-as-judge (Haiku), report-only initially:** Haiku grades the card against the scenario's
     expected-answer criteria and returns PASS / PARTIAL / FAIL + a 0/0.5/1 score + one-line reason.
     Scores are aggregated into a diagnostics report. Once a baseline run establishes the real mean,
     the eval graduates to asserting a held-out floor — the same baseline→floor methodology used for
     the boundary classifier.

**Out of scope:** wiring the full `IAnswerProvider` / `IScreenFollowUpClassifier` runtime path
(we call `PromptBuilderService` + the HTTP API directly, as Spec B does — keeps the test honest about
*which* prompt is sent); UI; asserting exact card prose (probabilistic — that is what the judge and
report-only tier are for).

## Design

### Corpus

A JSON corpus `tests/AIHelperNET.Integration.Tests/Eval/screen-answer-corpus.json`, mirroring the
existing `boundary-corpus.json` convention (versioned, human-editable). Each entry:

```jsonc
{
  "id": "sql-student-location-access",
  "mode": "SolveCodingTask",
  "profile": { "language": null, "backend": null, "frontend": null },
  "interviewerSpeech": "write a SQL to get the students who has an access to the location more than 2",
  "screenOcr": "Student Table\nID Name Age\n...\nStudentLocAccess\nId StudId LocationId\n...",
  "followUpSpeech": "now select only distinct values",   // optional
  "priorAnswerForFollowUp": "SELECT s.Name FROM Student s ...",  // optional; seeds the follow-up turn
  "requireCodeFence": "sql",          // null when no code is expected (e.g. system design)
  "requiredSubstrings": ["DISTINCT"], // case-insensitive deterministic gate; [] when none
  "expectedCriteria": "Correct SQL returning students accessing more than 2 distinct locations. With the given data only student C (id 3, 3 locations) qualifies. Must group/count per student and filter COUNT > 2."
}
```

The corpus is a superset of the `InterviewScenarioTests` catalog (it adds grading fields and lives
in the Integration project, which does not reference the App.Tests project). The interviewer-speech /
OCR overlap is intentional duplication: the deterministic tests own the pre-model assertions in code;
the eval corpus owns the live grading data as versioned JSON. Ground-truth notes per docx scenario:

- **SQL student-location-access** → only student **C** (id 3) has access to > 2 locations; follow-up
  adds `DISTINCT`.
- **SQL find-the-main-manager** → top of the `A→B→C→D` chain is **D**; the `X→E→Y→X` cycle must not
  loop forever.
- **C# super-manager** → walk the manager chain to the top; `Input A ⇒ Output D`; detect the
  `Y→X` cycle with a visited set.

### Test class

`tests/AIHelperNET.Integration.Tests/Eval/ScreenAnswerCardLiveTests.cs`,
`[Trait("Category", "LiveLlm")]` (excluded from fast runs). One `[Fact]` iterates the corpus:

- Reads the key from Windows Credential Manager via `WindowsCredentialSecretStore` (the Spec B
  mechanism — no env-var plumbing). Self-skips with a logged `Skipped:` line when absent.
- Generates each card with the production model; for `followUpSpeech` entries, generates the base
  card first, then the follow-up card via `BuildScreenFollowUp` seeded with `priorAnswerForFollowUp`.
- Applies the deterministic gates, then calls Haiku (`claude-haiku-4-5-20251001`) as judge.
- Writes a report to the diagnostics dir under the data root (`...\AIHelperNET\diagnostics\`,
  e.g. `screen-answer-eval-*.txt`) and echoes it via `console;verbosity=detailed`, matching the
  other evals.

### The judge

A small helper sends Haiku a strict rubric: the `expectedCriteria` plus the generated card **fenced
and labeled as untrusted data**, and asks for JSON `{ "verdict": "...", "score": 0|0.5|1, "reason": "..." }`.
It strips a leading ```json fence before parsing — the exact bug that silently broke the boundary
classifier (a fenced response that never parsed and fell back). Judge model output is display/score
only; it drives no privileged action.

### Scoring & assertions

- **Deterministic gates:** enforced with FluentAssertions when a key is present — these are reliable
  and a failure is a real regression (truncated card, missing code block, dropped `DISTINCT`).
- **Judge score:** **report-only on first landing** (establish the baseline mean, as the boundary
  eval did before fixing a threshold). A follow-up commit graduates it to assert a held-out mean
  floor (proposed ≥ 0.8) once the baseline is observed — written explicitly so we don't enshrine an
  unvalidated number. Garbled/fuzzy cases stay report-only by design (the Spec B / boundary lesson:
  don't assert probabilistic prose).

## Testing

`dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~ScreenAnswerCardLiveTests"`
— skips cleanly with no key (CI), and with the key produces the report + enforces the deterministic
gates. The `run-ai-eval` skill is the operational guide (key bridge, reading the report); it gains a
row for this family.

## Security

- Synthetic fixtures only — no transcripts/PII in the corpus or reports.
- The card is fenced and labeled as data inside the judge prompt; untrusted text is never blurred
  into the judge's instruction section (standing prompt-injection rule).
- Key read in-process from Credential Manager, never echoed, logged, or committed; reports are
  metadata + model text only. No new privileged action is driven by model output.

## Risks / notes

- Live evals cost tokens and are non-deterministic in prose — hence opt-in, report-first, and
  deterministic-gated where it counts.
- Generating with `claude-opus-4-8` (production default) makes the eval faithful but slower/costlier
  than a Haiku-only run; acceptable for an opt-in harness. If cost matters, the generator model can
  be overridden via `ClaudeOptions` in the test without changing the design.
- The corpus duplicates scenario speech/OCR from `InterviewScenarioTests`; if that drifts, a shared
  source could be extracted later (YAGNI for now — two consumers, different projects).
