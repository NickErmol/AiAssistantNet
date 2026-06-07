# Design: Implicit Question Detection + Context-Aware Prompting

**Date:** 2026-06-07  
**Status:** Approved  
**Branch target:** `develop`

---

## Problem Statement

Two independent gaps in the pipeline cause the AI to produce unhelpful answers.

### Gap 1 — Implicit imperative phrases not detected as questions

Phrases like "You tell me about such patterns as fabric decorator builder" are never classified as questions because `QuestionBoundaryDetector` Rule 10 checks only the *first* word against the `Imperatives` set. The first word `"you"` is in neither `Imperatives` nor `Interrogatives`, so the segment falls through to Rule 12 (low-confidence `NoQuestion`), sending it to the AI boundary classifier or silently discarding it.

Affected patterns:
- **Direct indirect imperative**: "You tell me about X", "You explain how X works"
- **Polite interrogative**: "Can you describe X", "Could you walk me through X", "Would you show me X"

### Gap 2 — Answer prompts lack conversational context

`PromptBuilderService.Build()` sends only the bare question text to the AI. When a short question like "What is the pattern?" arrives after a multi-turn conversation about Builder/Decorator/Factory, the AI has no context and responds with "I need more context…" — exactly the wrong behaviour for a live interview assistant.

---

## Design

### Fix 1 — "You [imperative]" and "Can/Could/Would you [imperative]" detection

**Location:** `QuestionBoundaryDetector.Evaluate()` — insert **new Rule 9.5** between Rule 9 and Rule 10.

**Logic:**

```
words[0] == "you"  AND  Imperatives.Contains(words[1])  AND  words.Length >= 5
  → TaskComplete (confidence 0.85)
  → reason: "Indirect imperative 'you [verb]'"

words[0] IN {"can","could","would","will"}  AND  words[1] == "you"
  AND  Imperatives.Contains(words[2])  AND  words.Length >= 6
  → QuestionComplete (confidence 0.85)
  → reason: "Polite interrogative '[modal] you [verb]'"
```

**Examples:**

| Input | Result |
|---|---|
| "You tell me about such patterns as fabric decorator builder" | `TaskComplete` ✓ |
| "You explain how the builder pattern works" | `TaskComplete` ✓ |
| "Can you describe the factory pattern" | `QuestionComplete` ✓ |
| "Could you walk me through CQRS" | `QuestionComplete` ✓ |
| "You are right about that" | no match — `"are"` not in Imperatives ✓ |
| "You know what I mean" | no match — `"know"` not in Imperatives ✓ |

**Words[n] access:** guard with `words.Length > n` before each index access.

**Tests:** add cases to `QuestionBoundaryDetectorTests` covering both patterns, the word-count guards, and the safe non-matches above.

---

### Fix 2 — Context-aware prompt building

#### 2a. `PromptBuilderService.Build()` — add context parameters

Add two optional parameters to the existing `Build(CodeProfile, AnswerSettings, string, string?)` overload:

```csharp
public static AnswerPrompt Build(
    CodeProfile profile,
    AnswerSettings settings,
    string questionText,
    IReadOnlyList<TranscriptItem>? recentTranscript = null,
    IReadOnlyList<(string Question, string Answer)>? recentQA = null,
    string? screenContext = null)
```

When context is provided, inject a **"Conversation context"** block into the user message **before** the question:

```
Conversation context (recent discussion):
[Transcript] Me: You tell me about such patterns as fabric decorator builder.
[Transcript] Interviewer: What is the pattern?
[Q&A] Q: What is a design pattern?  A: A design pattern is a reusable solution…

Question: What is the pattern?
```

Rules:
- Transcript lines: format as `[Transcript] {speaker}: {text}` — include only if `recentTranscript` is non-empty.
- Q&A lines: format as `[Q&A] Q: {question}  A: {answer}` — cap each answer at **200 characters** to bound tokens. Include only if `recentQA` is non-empty.
- Both blocks are separated from the question by a blank line.
- The existing `Build(CodeProfile, AnswerSettings, DetectedQuestion, string?)` overload delegates to the new signature with `recentTranscript: null, recentQA: null` — no behaviour change for callers that don't opt in.

Speaker display: `Speaker.Me` → `"Me"`, `Speaker.Other` → `"Interviewer"`.

#### 2b. `GenerateAnswerHandler` — collect and pass context

After loading the session and resolving the turn, collect:

**Recent transcript** — last 5 `TranscriptItem`s from `session.TranscriptItems` where `Timestamp <= turn.CreatedAt` (or all items if `CreatedAt` unavailable), ordered ascending by timestamp.

**Recent Q&A** — last 2 completed `ConversationTurn`s (status `PreliminaryReady` or `RefinedReady` or `Resolved`) that are **not** the current turn, ordered ascending by creation time. For each: `turn.InitialQuestionText` as Q, latest `AnswerVersion.Text` (first 200 chars) as A.

Pass both to `PromptBuilderService.Build()`.

**Token budget estimate:** 5 transcript lines ≈ 60–120 tokens; 2 Q&A pairs ≈ 80–200 tokens. Total overhead ≤ ~320 tokens — well within the smallest `AnswerLength` budget (150 tokens output, but system+user input has no hard cap).

#### 2c. `Session` domain — verify `TranscriptItems` is accessible

`Session` already exposes `TranscriptItems` (populated via `AddTranscriptItem`). Confirm the EF Core mapping loads them in `GetAsync`. If they are currently excluded from the query (e.g., not `.Include()`d), add the include. **No new columns or migrations needed.**

---

## What is NOT in scope

- Multi-turn conversation format for `IAnswerProvider` (would require API contract changes).
- Sending the full session transcript (only last 5 items — bounded by design).
- Changing the `BuildFollowUp` or `BuildWithScreenMode` prompt paths.
- UI changes.

---

## Testing

### Unit tests (`QuestionBoundaryDetectorTests`)
- "You tell me about X" (5 words) → `TaskComplete`
- "You explain how the builder pattern works" → `TaskComplete`
- "Can you describe the factory pattern" → `QuestionComplete`
- "Could you walk me through CQRS architecture" → `QuestionComplete`
- "You are right about that" → not `TaskComplete` (safe non-match)
- "You know" (2 words) → word-count guard fires, not `TaskComplete`
- "Can you" (2 words) → word-count guard fires, not `QuestionComplete`

### Unit tests (`PromptBuilderServiceTests` — new file)
- `Build()` with no context → user message contains only `Question: {text}`
- `Build()` with transcript only → context block present, Q&A block absent
- `Build()` with Q&A only → context block present, transcript block absent
- `Build()` with both → both blocks present, answer capped at 200 chars
- Long answer (>200 chars) → truncated in prompt

### Integration / E2E
- Run `e2e-test` skill from `develop` after merge to verify no pipeline regression.
