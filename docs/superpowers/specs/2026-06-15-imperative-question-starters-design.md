# Imperative Question Starters — Design

**Date:** 2026-06-15
**Branch:** `feature/imperative-question-starters` (off `develop`)
**Status:** Approved design — pending implementation plan

## Goal

Recognize imperative / command-style and otherwise *non-obvious* requests as answerable
tasks in the live interview pipeline. Today the heuristic catches direct interrogatives
("what is X?") and a small set of imperative verbs, but misses many natural phrasings an
interviewer uses:

```
Explain …  Describe …  Compare …  Show …  Give me …  Provide …  List …  Name …
Define …  Clarify …  Break down …  Walk me through …  Analyze …  Review …
Summarize …  Translate …  Convert …  Rewrite …  Fix …  Find …  Create …
Generate …  Write …  Implement …
```

…plus the harder, verb-less cases from the richer rubric we compared against:

- Polite prefixes: "please explain …", "could you …", "can you …".
- Short technical topics that *imply* explanation: "N+1 queries", "Change detection and OnPush",
  "Func vs Expression<Func>".
- Follow-up refinement phrases: "what about …", "go deeper", "more details", "with examples".
- Code + an implying phrase: "why this works", "what will be the output".
- And the negative: bare acknowledgements ("ok", "thanks", "got it") must stay filtered.

## Why two layers (the core decision)

Detection splits cleanly by *what kind of judgment it needs*:

- **Deterministic, first-word / prefix rules** can reliably handle verbs, polite prefixes, and
  acknowledgements. No false-positive flood, no API cost. → the heuristic `QuestionBoundaryDetector`.
- **Semantic judgment** is required for "is `N+1 queries` a request for explanation?" or "does this
  code + 'why this works' imply explain-this?". A first-word rule can't do this without matching
  *every* noun phrase. → the existing Haiku `QuestionBoundaryClassifier` (the LLM fallback).

The architecture already supports this: the pipeline calls the AI classifier whenever the
heuristic returns confidence `< 0.7` (`TranscriptPipelineService` line ~197-198). So the semantic
layer is an *enrichment*, not new infrastructure. The catch: some heuristic short-circuits
(notably the `< 4 words → Unrelated @ 0.95` rule) fire *before* the AI is ever consulted, starving
it of exactly the short-topic cases we want it to judge.

For a live interview, a **missed question is worse than an extra card** — a missed answer is the
exact failure this tool exists to prevent; a spurious card is a glance you dismiss. That asymmetry
justifies the broader net, hence including the semantic layer rather than verbs alone.

## Two-phase plan

Phase 1 is deterministic and carries **zero eval risk** — it ships on its own value. Phase 2 is the
semantic layer and requires a live-eval re-run. If Phase 2 regresses the eval, Phase 1 still stands.

### Phase 1 — Deterministic (heuristic only)

**1a. Unify the imperative verb list.** Today two near-duplicate, already-drifted sets exist:
`QuestionDetector.ImperativeVerbs` (13) and `QuestionBoundaryDetector.Imperatives` (19). Extract a
single internal source of truth in the Domain layer — `Domain/Questions/QuestionLexicon.cs` (a
static class exposing `ImperativeVerbs`, plus the new politeness + multi-word phrase data below).
Both detectors consume it. Eliminates future drift.

**1b. Expand the verb set** to the union covering the full requested list. New single verbs to add:
`provide, list, name, define, clarify, summarize, translate, convert, rewrite, find, generate, review`
(existing already covers `explain, describe, compare, show, give, walk, analyze, fix, create, write,
implement, design, debug, optimize, refactor, build, outline, discuss, tell`).

**1c. Multi-word imperative phrase: `break down`.** This is the only requested multi-word starter
whose leading token (`break`) is not a safe standalone imperative. Add a small phrase list
(`break down …`) checked as a prefix. The others already match on their leading verb
(`walk me through` → `walk`, `give me` → `give`, `show me` → `show`).

**1d. Polite-prefix stripping.** Before the first-word imperative/interrogative checks, strip a
leading politeness token/phrase: `please`, `kindly`, `could you`, `can you`, `would you`, `can you
please`, etc. Then re-evaluate the *remaining* first word. This makes "please explain the GC" →
`explain …` → `TaskComplete`. (Cases like "can you explain …" already classify as a question via
the `can` interrogative; stripping just routes them to `TaskComplete` instead — both generate an
answer, so behavior is preserved.)

**1e. Lower the word-count floor to 2 for the imperative path only.** Today an imperative needs
`≥ 4` words (Rule 10). Short commands ("Define recursion", "List the principles", "Name three
algorithms") are 2-3 words and get dropped. Lower the imperative floor to `≥ 2`. The interrogative
gates (`≥ 4` / `≥ 6` words) are untouched. The same change applies to `QuestionDetector.MinWords`
on its imperative branch.

**1f. Rule 2 exemption (required for 1e to take effect).** Rule 2 (`< 4 words → Unrelated @ 0.95`)
currently fires *before* the imperative rule, so a 2-word imperative would be killed as `Unrelated`
first. Add an exemption: a `< 4` word segment is only `Unrelated` if its (politeness-stripped)
first word is **not** a recognized imperative verb and it does not start with a `break down` phrase.
Filler (Rule 3) still runs first, so "got it" / "thanks" stay filtered.

**Phase 1 covers rubric rules 1, 2, 3 and the negative (acknowledgements) fully.**

### Phase 2 — Semantic (AI classifier + unblocking)

**2a. Enrich the Haiku `SystemPrompt`** in `QuestionBoundaryClassifier` with the rubric's
semantic cases, as labels + examples:
- Short technical topic implying explanation → `QuestionComplete`/`TaskComplete`
  (e.g. `latest:"N+1 queries" → QuestionComplete (topic stated as a request to explain)`).
- Code present + an implying phrase ("why this works", "what will be the output") → answerable.
- Follow-up refinement phrases ("go deeper", "more details", "with examples") on a live/answered
  turn → `QuestionContinued` / `AdditionalRequirement` (refine the current card, not a new one).

**2b. Relax the Rule 2 short-circuit so short topics reach the AI.** A `< 4` word segment that is
**not** a known filler/acknowledgement and **looks topic-like** should be emitted as `Unrelated`
at **low confidence (< 0.7)** instead of 0.95, so the pipeline routes it to the AI classifier.
"Topic-like" is a cheap deterministic sniff to bound API cost — e.g. contains a capitalized
technical term, a code-ish token (`<`, `>`, `()`, `::`), a `vs`/`versus`, or a mixed
alphanumeric token like `N+1`. Pure short filler keeps its high-confidence `Unrelated` and never
hits the API. **This sniff is the main cost/noise knob** and the primary thing to validate.

**2c. Re-run the live boundary eval** (`run-ai-eval`) and confirm the held-out set stays at its
12/12 floor and the garbled corpus does not regress.

## Components touched

| File | Change | Phase |
|---|---|---|
| `Domain/Questions/QuestionLexicon.cs` (new) | Shared imperative verbs + politeness + `break down` phrase | 1 |
| `Domain/Questions/QuestionBoundaryDetector.cs` | Consume lexicon; politeness strip; 2-word imperative floor; Rule 2 exemption + (2b) topic-like low-confidence | 1, 2 |
| `Domain/Questions/QuestionDetector.cs` | Consume lexicon; 2-word imperative floor | 1 |
| `Infrastructure/AI/QuestionBoundaryClassifier.cs` | Enrich `SystemPrompt` (2a) | 2 |
| `tests/AIHelperNET.Domain.Tests/Questions/*` | New heuristic cases | 1 |
| Eval corpora / `run-ai-eval` | Validate no regression | 2 |

## Out of scope

- No new user settings, no EF/migration changes.
- No change to answer generation, prompts (other than the classifier), or UI.
- No change to the `Me`-speaker clarification routing (Rule 4) or scenario-setup rules.

## Testing (TDD)

**Phase 1 — `QuestionBoundaryDetectorTests` / `QuestionDetectorTests`:**
- Each newly added verb at the start → `TaskComplete` / new task (table-driven).
- Short 2-word imperative ("Define recursion") → `TaskComplete`, not `Unrelated`.
- `break down the auth flow` → `TaskComplete`.
- `please explain the GC` / `could you list the SOLID principles` → `TaskComplete`.
- Negatives unchanged: `got it`, `thanks`, `ok` → `Unrelated`; interrogative gates unchanged.

**Phase 2 — boundary eval (`run-ai-eval`):**
- Capture baseline first; add short-topic / code-implies / follow-up rows to the eval corpus.
- Held-out floor stays 12/12; garbled corpus no worse.

## Risks

- **Phase 2 false positives / API cost** (2b): the topic-like sniff is the knob. Mitigated by
  running filler/ack guards first and gating on technical-looking tokens. Validate via eval before
  merge; ship Phase 1 independently if Phase 2 is shaky.
- **Drift already exists** between the two verb sets (1a fixes the root cause).
