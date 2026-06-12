# Boundary-Classifier Tuning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cut the boundary-classifier mislabel rate (Issue D) by expanding the labeled corpus, recording the first real Haiku baseline, and enriching the bare-bones classifier system prompt with per-label definitions + few-shot examples + an anti-over-split tie-breaker — all measured before/after.

**Architecture:** Two deterministic, CI-guarded code changes (corpus data + one system-prompt const) bracketed by two human-in-the-loop measurement checkpoints (the real-Haiku eval needs an Anthropic API key in an env var and so cannot run in CI). The deterministic heuristic regression test keeps the corpus honest every commit; the opt-in Haiku eval (built in Spec 3b) measures the prompt change.

**Tech Stack:** .NET 10, C# latest (raw string literals), xUnit + FluentAssertions, the existing `Eval/` harness (`CorpusLoader`, `ConfusionMatrix`, `EvalReport`, `BoundaryHeuristicEvalTests`, `BoundaryClassifierAiEvalTests`).

**Spec:** `docs/superpowers/specs/2026-06-11-boundary-classifier-tuning-design.md`

---

## File Structure

**Modified files**
- `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json` — +31 labeled entries (24 → 55), over-sampling the over-split confusions. Pure test data; copied next to the test assembly already.
- `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs:11-12` — re-measured `Baseline` constant (the new corpus intentionally contains cases beyond the heuristic's reach, so the regression floor is re-baselined to the new measured value, floored to the 0.05 step — same convention already in the file).
- `src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs:25-29` — `SystemPrompt` rewritten as a raw string literal with label definitions, few-shot examples, and an anti-over-split tie-breaker. No schema/label/parse change.

**Unchanged (explicitly out of scope):** pipeline, Domain, EF, `BoundarySplitGuard`, the eval harness code itself, the classifier's request/response/parse logic, the model id.

**Two human checkpoints (no code):** Task 2 (record Haiku baseline) and Task 4 (post-tune re-measure + accept/iterate). Each gives the user an exact command and states what to capture/decide.

---

## Task 1: Expand the corpus (deterministic, CI-guarded)

Add 31 entries to reach 55, every label ≥ 6, over-sampling `QuestionContinued` (the over-split label) and adding `NewQuestion` contrast + `Me` clarifications. Then re-baseline the heuristic regression floor, because some new entries are deliberately beyond the simple heuristic's reach (that is *why* the AI classifier exists).

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json`
- Modify: `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs:11-12`

- [ ] **Step 1: Add the 31 entries to the corpus**

Open `boundary-corpus.json`. It is a JSON array; the last entry (`noise-statement`) currently ends with `}` on its own line, followed by the closing `]`. Insert the block below **between** that final `}` and the `]` — the block already begins with a leading comma that separates it from the `noise-statement` entry, so do not add any other comma (a double comma is invalid JSON):

```json
,
  { "id": "cont-ratelimit-burst", "recentItems": [ { "speaker": "Other", "text": "design a rate limiter for our gateway" } ], "latestItem": { "speaker": "Other", "text": "and how would it handle sudden bursts of traffic" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: refinement reads like a new question" },
  { "id": "cont-auth-refresh", "recentItems": [ { "speaker": "Other", "text": "how would you design authentication for the api" } ], "latestItem": { "speaker": "Other", "text": "so then how do refresh tokens fit into that" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: same auth topic extended" },
  { "id": "cont-db-sharding", "recentItems": [ { "speaker": "Other", "text": "let's design the data layer for this" } ], "latestItem": { "speaker": "Other", "text": "in that case how would you shard the database" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: continuation cue 'in that case'" },
  { "id": "cont-cache-ttl", "recentItems": [ { "speaker": "Other", "text": "talk about your caching approach" } ], "latestItem": { "speaker": "Other", "text": "what ttl would you pick for those entries" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: detail question on same topic" },
  { "id": "cont-queue-ordering", "recentItems": [ { "speaker": "Other", "text": "we're using a message queue here" } ], "latestItem": { "speaker": "Other", "text": "how do you guarantee ordering across partitions" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "QuestionContinued", "note": "over-split while collecting" },
  { "id": "cont-search-rank", "recentItems": [ { "speaker": "Other", "text": "design the search feature" } ], "latestItem": { "speaker": "Other", "text": "and how would you rank the results" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: refinement of same feature" },
  { "id": "cont-deploy-rollback", "recentItems": [ { "speaker": "Other", "text": "walk me through the deployment pipeline" } ], "latestItem": { "speaker": "Other", "text": "what about rolling back a bad release" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "over-split: 'what about' continuation" },
  { "id": "cont-graphql-nplus1", "recentItems": [ { "speaker": "Other", "text": "let's say we expose a graphql api" } ], "latestItem": { "speaker": "Other", "text": "how would you avoid the n plus one problem there" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "QuestionContinued", "note": "over-split: 'there' ties to same scenario" },
  { "id": "addreq-latency", "recentItems": [ { "speaker": "Other", "text": "design the notification service" } ], "latestItem": { "speaker": "Other", "text": "also it needs to stay under 100 milliseconds p99" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new latency constraint on answered turn" },
  { "id": "addreq-multiregion", "recentItems": [ { "speaker": "Other", "text": "how would you store user sessions" } ], "latestItem": { "speaker": "Other", "text": "also assume we run in three regions" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new deployment constraint" },
  { "id": "addreq-gdpr", "recentItems": [ { "speaker": "Other", "text": "design the analytics pipeline" } ], "latestItem": { "speaker": "Other", "text": "and it has to be gdpr compliant too" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new compliance constraint" },
  { "id": "addreq-budget", "recentItems": [ { "speaker": "Other", "text": "pick a database for this workload" } ], "latestItem": { "speaker": "Other", "text": "keep in mind we have a tight cloud budget" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new cost constraint" },
  { "id": "addreq-offline", "recentItems": [ { "speaker": "Other", "text": "design the mobile sync feature" } ], "latestItem": { "speaker": "Other", "text": "it should also work fully offline" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new offline constraint" },
  { "id": "newq-moveon-testing", "recentItems": [ { "speaker": "Other", "text": "explain how the event loop works" } ], "latestItem": { "speaker": "Other", "text": "moving on, how do you approach testing async code" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "explicit 'moving on' + new topic" },
  { "id": "newq-next-security", "recentItems": [ { "speaker": "Other", "text": "design a url shortener" } ], "latestItem": { "speaker": "Other", "text": "next question, how would you secure this api" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "explicit 'next question' marker" },
  { "id": "newq-switch-frontend", "recentItems": [ { "speaker": "Other", "text": "we covered the backend, good" } ], "latestItem": { "speaker": "Other", "text": "let's switch gears, what's your react experience" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "explicit 'switch gears' + new topic" },
  { "id": "start-ecommerce", "recentItems": [], "latestItem": { "speaker": "Other", "text": "suppose we're building an ecommerce checkout flow" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup, not yet answerable" },
  { "id": "start-streaming", "recentItems": [], "latestItem": { "speaker": "Other", "text": "let's say you're designing a video streaming backend" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup" },
  { "id": "start-bank", "recentItems": [], "latestItem": { "speaker": "Other", "text": "picture a banking system that moves money between accounts" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup" },
  { "id": "start-iot", "recentItems": [], "latestItem": { "speaker": "Other", "text": "imagine thousands of iot devices reporting telemetry" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup" },
  { "id": "clarif-me-sync", "recentItems": [ { "speaker": "Other", "text": "how would you design the file upload" } ], "latestItem": { "speaker": "Me", "text": "do you mean synchronous or chunked uploads" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarifies scope" },
  { "id": "clarif-me-scale", "recentItems": [ { "speaker": "Other", "text": "how would you handle the load" } ], "latestItem": { "speaker": "Me", "text": "are we talking about scaling reads or writes" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarification" },
  { "id": "clarif-me-lang", "recentItems": [ { "speaker": "Other", "text": "implement a cache" } ], "latestItem": { "speaker": "Me", "text": "should i do this in c sharp or pseudocode" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarifies format" },
  { "id": "clarif-me-constraints", "recentItems": [ { "speaker": "Other", "text": "design the api" } ], "latestItem": { "speaker": "Me", "text": "any constraints on the tech stack i should assume" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarifies constraints" },
  { "id": "qcomplete-async", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what is the difference between async and parallel programming" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "direct question" },
  { "id": "qcomplete-index", "recentItems": [], "latestItem": { "speaker": "Other", "text": "when would you add a database index" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "direct question" },
  { "id": "qcomplete-rest", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what makes an api restful" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "direct question" },
  { "id": "task-reverse-ll", "recentItems": [], "latestItem": { "speaker": "Other", "text": "write a function to reverse a linked list" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative coding task" },
  { "id": "task-design-cache", "recentItems": [], "latestItem": { "speaker": "Other", "text": "design an lru cache with o of one operations" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative design task" },
  { "id": "task-sql-topn", "recentItems": [], "latestItem": { "speaker": "Other", "text": "write a sql query for the top three earners per department" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative task" },
  { "id": "filler-takeyourtime", "recentItems": [], "latestItem": { "speaker": "Other", "text": "no rush take your time" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "social filler" }
```

- [ ] **Step 2: Verify the corpus loads and is well-formed**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~CorpusLoaderTests" --nologo`
Expected: PASS (2 tests). This proves the JSON parses, ids are unique, no blank texts, and ≥6 distinct labels. If it FAILS to parse, the most likely cause is a missing/extra comma where you spliced the array — fix that.

- [ ] **Step 3: Run the heuristic eval to read the new measured accuracy**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryHeuristicEvalTests" --nologo -l "console;verbosity=detailed"`
Expected: it prints `=== Heuristic (QuestionBoundaryDetector) ===` with a `Accuracy=NN.N%` line. The test **may now FAIL** the `>= 0.80` assertion — that is expected, because the new corpus deliberately includes over-split continuations the simple heuristic cannot catch (those are the AI classifier's job). Record the printed `Accuracy` value; you need it for the next step.

- [ ] **Step 4: Re-baseline the heuristic regression floor**

In `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs`, replace lines 11-12:

```csharp
    // measured 2026-06-09: 0.833  (floored to the 0.05 step below)
    private const double Baseline = 0.80;
```

with (substitute `<MEASURED>` with the accuracy from Step 3, floored to the nearest 0.05 step — e.g. a measured 0.71 becomes 0.70):

```csharp
    // measured 2026-06-11 over the expanded 55-entry corpus: <MEASURED> (floored to the 0.05 step
    // below). The corpus now intentionally contains over-split continuations beyond the simple
    // heuristic's reach — the AI classifier (Spec 3d) is what catches those. This floor is a
    // regression guard, not a target.
    private const double Baseline = <FLOORED>;
```

> Only ever floor to the measured value; never set the floor above what the heuristic actually scores. If the measured accuracy went *up* or stayed ≥0.80, keep `0.80` (do not raise it) and update only the comment/date.

- [ ] **Step 5: Confirm the heuristic test is green at the new floor**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryHeuristicEvalTests" --nologo`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs
git commit -m "test(eval): expand boundary corpus to 55 entries; re-baseline heuristic floor"
```

---

## Task 2: Record the real-Haiku baseline (HUMAN CHECKPOINT — no code)

There is no recorded Haiku accuracy yet. Before tuning the prompt, capture the "before" over the expanded corpus. This needs an Anthropic API key and so cannot run in CI — the user runs it.

**Files:** none.

- [ ] **Step 1: Give the user the exact eval command**

Ask the user to run this in the session (PowerShell), substituting their key. Using the `!` prefix puts the output in-session:

```
! $env:AIHELPER_AI_EVAL_KEY = "sk-ant-..."; dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests" --nologo -l "console;verbosity=detailed"
```

> The key is read only from the `AIHELPER_AI_EVAL_KEY` env var by `BoundaryClassifierAiEvalTests` and is never written to the repo. If the var is unset the test skips trivially (prints a "Skipped:" line) — that means the key was not exported into the same shell; retry as one command as shown.

- [ ] **Step 2: Capture the baseline numbers**

From the printed report (also saved to `AppPaths.DiagnosticsDir/ai-eval-<timestamp>.txt`), record into the plan/PR notes:
1. The overall `Accuracy=NN.N%`.
2. The **over-split confusion cells** from the `Confusion` block — specifically any `... -> NewQuestion` lines whose expected label is `QuestionContinued` or `AdditionalRequirement` (these are the over-splits), and the `Misses:` list.

This is the "before". Do not change any code in this task.

- [ ] **Step 3: Checkpoint — stop and confirm**

Confirm with the user that the baseline report was captured before proceeding. If the eval reported many `API failures`, resolve those (bad key, network) and re-run before tuning — a baseline built on failures is not usable.

---

## Task 3: Enrich the classifier system prompt

Rewrite `SystemPrompt` from a bare label list into definitions + few-shot examples + an anti-over-split tie-breaker. The output schema, label set, and parsing are unchanged.

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs:25-29`

- [ ] **Step 1: Replace the SystemPrompt constant**

In `QuestionBoundaryClassifier.cs`, replace the existing constant (lines 25-29):

```csharp
    private const string SystemPrompt =
        "You are classifying a live technical interview speech segment for boundary detection.\n" +
        "You must return valid JSON only — no prose, no markdown, no code blocks.\n" +
        "Valid labels: NoQuestion | QuestionStarted | QuestionContinued | QuestionComplete | TaskComplete | ClarificationOfCurrentQuestion | AdditionalRequirement | NewQuestion | Unrelated\n" +
        "JSON schema: {\"classification\":\"<label>\",\"confidence\":<0.0-1.0>,\"normalized_text\":\"<trimmed input>\",\"reason\":\"<one short sentence>\"}";
```

with this raw string literal (note: a raw string literal const needs no escaping for quotes or braces):

```csharp
    private const string SystemPrompt =
        """
        You are classifying one speech segment from a live technical interview to detect question boundaries.
        Return VALID JSON ONLY — no prose, no markdown, no code fences.

        You are given: active_turn_status (the state of the question currently being tracked, or null),
        speaker_of_latest (Other = interviewer, Me = candidate), up to 5 recent_items for context,
        and text_to_classify (the latest item).

        Labels and when to use each:
        - NoQuestion: speech that is not part of any question and starts nothing (rare; prefer Unrelated for filler).
        - QuestionStarted: the interviewer is setting up a scenario not yet answerable on its own ("suppose we have...", "imagine a system that...").
        - QuestionContinued: the latest item extends, refines, or adds detail to the SAME question/scenario already in progress — even if phrased like a standalone question. Default when the subject matches the active turn.
        - QuestionComplete: a complete, answerable question stated on its own ("what is dependency injection?").
        - TaskComplete: a complete, answerable imperative/coding task ("write a function to reverse a linked list", "design an LRU cache").
        - ClarificationOfCurrentQuestion: usually the candidate (Me) asking the interviewer to clarify the scope of the current question ("do you mean reads or writes?").
        - AdditionalRequirement: a NEW constraint added to a question already asked/answered ("also it must be idempotent", "keep it under 100ms").
        - NewQuestion: a genuinely different topic the prior turn did not cover — typically after the prior turn is answered, usually flagged by an explicit shift marker ("moving on", "next question", "different topic", "let's switch gears").
        - Unrelated: social filler, acknowledgements, or logistics ("thanks for sharing", "can you hear me?", "take your time").

        Tie-breaker (important — avoids over-splitting one question into two cards):
        When active_turn_status indicates a live or just-answered turn and the latest item stays on the SAME
        subject/scenario as recent_items, prefer QuestionContinued or AdditionalRequirement over NewQuestion.
        Choose NewQuestion only when the topic clearly changes OR there is an explicit topic-shift marker.

        Examples (input -> correct label):
        - recent:["let's talk about caching strategies"] latest:"how would you handle cache invalidation here" status:PreliminaryReady -> QuestionContinued (same caching topic, no shift marker)
        - recent:["design a notification service"] latest:"also it needs to stay under 100 milliseconds p99" status:PreliminaryReady -> AdditionalRequirement (new constraint on the same task)
        - recent:["explain how dependency injection works"] latest:"completely different topic, what's your experience with kubernetes?" status:PreliminaryReady -> NewQuestion (explicit shift + new topic)
        - recent:["how would you scale the database?"] latest(Me):"do you mean the read path or the write path?" status:CollectingQuestion -> ClarificationOfCurrentQuestion (candidate clarifies scope)
        - latest:"give me a second to share my screen" status:null -> Unrelated (logistics filler)

        JSON schema (return exactly this shape):
        {"classification":"<one label above>","confidence":<0.0-1.0>,"normalized_text":"<trimmed text_to_classify>","reason":"<one short sentence>"}
        """;
```

> If Task 2's miss list surfaced a confusion category not represented above (e.g. repeated `TaskComplete -> QuestionComplete` mistakes), add one more `input -> correct label` example line for it, mirroring the format. Keep the example count at 5-7; few-shot teaches distinctions, not memorization.

- [ ] **Step 2: Build to confirm it compiles (warnings are errors)**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj --nologo`
Expected: Build succeeded, 0 warnings, 0 errors. (`SystemPrompt` is a `private const` — no XML-doc requirement. A raw string literal is valid in a `const`.)

- [ ] **Step 3: Confirm no deterministic test regressed**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --nologo`
Expected: PASS. (No Infrastructure unit test asserts on the prompt text; this just confirms nothing else broke. The prompt's real effect is measured by the Haiku eval in Task 4.)

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs
git commit -m "feat(ai): enrich boundary-classifier prompt with label definitions, few-shot, anti-over-split tie-breaker"
```

---

## Task 4: Re-measure, accept-or-iterate, lock the numbers (HUMAN CHECKPOINT)

**Files:** none (unless iterating Task 3's examples).

- [ ] **Step 1: Re-run the same eval (user)**

Same command as Task 2 Step 1:

```
! $env:AIHELPER_AI_EVAL_KEY = "sk-ant-..."; dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests" --nologo -l "console;verbosity=detailed"
```

- [ ] **Step 2: Compare against the baseline (success criterion)**

Accept the prompt change ONLY if, versus the Task 2 baseline:
1. Overall `Accuracy` rose by a **meaningful margin** (not a single-entry 1.8% wobble — look for ≥ ~5 points or a clearly smaller miss list), AND
2. The over-split confusion cells (`QuestionContinued -> NewQuestion`, `AdditionalRequirement -> NewQuestion`) **shrank**.

- [ ] **Step 3: If criteria not met, iterate (return to Task 3)**

If accuracy rose but over-splits did not shrink (or vice-versa), edit the few-shot examples / tie-breaker wording in `QuestionBoundaryClassifier.cs` to target the specific remaining misses, re-commit (amend or new commit), and re-run Step 1. Do **not** accept a change that improves one axis by regressing the other. Stop iterating after the criterion is met or after ~3 rounds (then report the best result and ask the user how to proceed).

- [ ] **Step 4: Record the before/after numbers**

Append a short results block to the spec file `docs/superpowers/specs/2026-06-11-boundary-classifier-tuning-design.md` under a new `## Results` heading: baseline accuracy + over-split cells, post-tune accuracy + over-split cells, and the date. (The Haiku number is documented, not CI-asserted — it is non-deterministic and costs money to produce.)

```bash
git add docs/superpowers/specs/2026-06-11-boundary-classifier-tuning-design.md
git commit -m "docs(spec): record boundary-classifier tuning before/after eval numbers"
```

---

## Task 5: Full verification + branch wrap-up

**Files:** none.

- [ ] **Step 1: Build the whole solution (warnings are errors)**

Run: `dotnet build --nologo`
Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 2: Run the full deterministic test suite**

Run: `dotnet test --nologo`
Expected: All green. `CorpusLoaderTests` and `BoundaryHeuristicEvalTests` pass at the new corpus/floor; `BoundaryClassifierAiEvalTests` passes trivially (skips — no key in CI). No count should drop.

- [ ] **Step 3: Confirm scope stayed tight**

Run: `git diff --stat develop`
Expected: only `boundary-corpus.json`, `BoundaryHeuristicEvalTests.cs`, `QuestionBoundaryClassifier.cs`, and the spec doc changed. If anything under `src/AIHelperNET.Application/Sessions/`, Domain, EF `Persistence/`, or `BoundarySplitGuard` changed, that is out of scope — revert it.

- [ ] **Step 4: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to open a PR from `feature/boundary-classifier-tuning` → `develop`. The PR description should include the recorded before/after Haiku numbers from Task 4 and note that CI guards only the deterministic heuristic (the AI eval is opt-in/human-run).

---

## Self-Review notes (for the executor)

- **Spec coverage:** Design §1 corpus → Task 1; §2 baseline → Task 2; §3 prompt enrichment → Task 3; §4 re-measure/lock → Task 4; acceptance #1 (corpus ≥55, labels ≥6) → Task 1; acceptance #2 (before/after reports) → Tasks 2+4; acceptance #3 (accuracy up + over-splits shrink) → Task 4 Step 2; acceptance #4 (scope) → Task 5 Step 3.
- **Non-determinism is real:** Tasks 2 and 4 cannot be automated or CI-guarded. They are explicit human checkpoints. Never convert the Haiku accuracy into a CI assertion.
- **Don't touch the guardrails:** `BoundarySplitGuard` (Spec 3a) is the deterministic safety net under the probabilistic layer you're tuning — leave it alone (Task 5 Step 3 enforces this).
- **Label/identifier consistency:** labels are the `BoundaryLabel` enum values exactly (`QuestionContinued`, `AdditionalRequirement`, `NewQuestion`, `QuestionComplete`, `TaskComplete`, `QuestionStarted`, `ClarificationOfCurrentQuestion`, `Unrelated`, `NoQuestion`); env var is `AIHELPER_AI_EVAL_KEY`; eval class is `BoundaryClassifierAiEvalTests`; heuristic test/const is `BoundaryHeuristicEvalTests.Baseline`.
