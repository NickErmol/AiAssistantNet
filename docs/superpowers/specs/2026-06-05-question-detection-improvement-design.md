# Question Detection Improvement — Design Spec

**Date:** 2026-06-05  
**Branch target:** `feature/question-detection-improvement` (from develop)  
**Scope:** Segment accumulator, LLM-based question classifier, continuation handling, pre-filter improvements

---

## Problem

Two problems observed from live session analysis:

1. **~30% false positive rate** — `QuestionDetector` triggers on any segment whose first word is an interrogative ("when", "how", "what"). This causes noise phrases like "When it works, it works", "So, what else?", "How do you feel, my question?" to create turns that the user must manually dismiss mid-interview.

2. **Missed question continuations** — Whisper splits long spoken questions across two VAD windows. Only the first segment is evaluated. The second ("Difference comparing then to promise") never triggers a turn, even though it is the meaningful part of the question.

---

## Approach: LLM Classification with Segment Stitching

New components buffer consecutive "Other" channel segments, pre-filter cheap cases with heuristics, then call Claude Haiku (~$0.001/call, ~400–700ms) to classify the combined text as `NewQuestion`, `Continuation`, or `NotAQuestion`.

Latency budget: up to ~1s of additional latency is acceptable.

---

## Architecture

The current pipeline:

```
AudioFrame → VAD → Whisper → TranscriptItem → TranscriptPipelineService → QuestionDetector → turn
```

The new pipeline:

```
AudioFrame → VAD → Whisper → TranscriptItem
    → TranscriptPipelineService
        → SegmentAccumulator       (new — buffers consecutive "Other" segments)
        → QuestionDetector         (existing — cheap pre-filter, no LLM call)
        → IQuestionClassifier      (new port — Haiku classification)
        → turn created / merged
```

### New Components

| Component | Layer | Purpose |
|---|---|---|
| `SegmentAccumulator` | `Application/Sessions/` | Buffers "Other" segments within a 3s gap; flushes combined text when gap exceeds threshold |
| `IQuestionClassifier` | `Application/Abstractions/` | Port — classifies combined text as `NewQuestion`, `Continuation`, or `NotAQuestion` |
| `HaikuQuestionClassifier` | `Infrastructure/AI/` | Implements `IQuestionClassifier` via a single Claude Haiku API call |

`QuestionDetector` is retained as a pre-filter gate. `IQuestionClassifier` is registered as a singleton in DI alongside `IAnswerProvider`.

---

## Section 1: SegmentAccumulator

Lives in `Application/Sessions/`. One instance per `TranscriptPipelineService` (per-session scoped).

**Behaviour:**
- When a `TranscriptItem` with `Speaker.Other` arrives, it is added to an internal buffer with its timestamp.
- If the gap between the previous segment's timestamp and the new one is **≤ 3 seconds**, the segment is appended to the current buffer.
- If the gap **exceeds 3 seconds**, the buffer is flushed (returns combined text), and a new buffer starts with the current segment.
- On flush, segments are joined with a single space: `"Can we use them and what is... Difference comparing then to promise."`

**API:**

```csharp
public sealed class SegmentAccumulator
{
    // Returns combined text if a flush occurred, null if segment was buffered.
    public string? Add(string text, DateTimeOffset timestamp);

    // Force-flush current buffer (called on session stop).
    public string? Flush();
}
```

**Edge cases:**
- Single segment with no follow-up within 3s → flushes normally as a single-segment string
- Max-window VAD flush mid-sentence → still stitched because timestamps are close
- Session stop → `Flush()` called before teardown

The 3s gap threshold is a named constant — not user-configurable in v1.

---

## Section 2: Full Classification Flow

### Step 1 — Pre-filter (no LLM call)

Before calling Haiku, `TranscriptPipelineService` runs the existing `QuestionDetector` on the combined text:

- Fewer than 4 words → discard
- Matches a known hallucination phrase (e.g. "software engineering, system design, coding") → discard
- `QuestionDetector.LooksLikeQuestion()` returns false → discard

Only if all three checks pass does the pipeline proceed to Haiku.

### Step 2 — Haiku Classification

```csharp
public interface IQuestionClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        string combinedText,
        IReadOnlyList<string> recentQuestions,
        CancellationToken ct);
}

public enum ClassificationResult { NewQuestion, Continuation, NotAQuestion }
```

The Haiku system prompt (~50 tokens):

> "You are classifying speech from a technical interview. Given the text below, reply with exactly one word: NewQuestion if it is a new interview question, Continuation if it continues or completes the previous question, or NotAQuestion if it is not a question at all. Recent questions for context: [Q1], [Q2]."

`recentQuestions` is the last 2 detected question texts from the session. Haiku returns a single word, parsed as `ClassificationResult`. Unknown response → `NotAQuestion`.

### Step 3 — Act on Result

| Result | Action |
|---|---|
| `NewQuestion` | Create `DetectedQuestion` + new `ConversationTurn`, fire `GenerateAnswerCommand(Preliminary)` — same as today |
| `Continuation` | Append combined text to active turn's `InitialQuestionText`, re-fire `GenerateAnswerCommand(Preliminary)` to supersede previous answer |
| `NotAQuestion` | Discard silently — no turn created |

`Continuation` only applies if there is an active open turn (any status except `AwaitingClarification` and `Dismissed`). If no active turn exists, `Continuation` is promoted to `NewQuestion`.

---

## Section 3: Error Handling

| Failure | Behaviour |
|---|---|
| Haiku call fails (timeout, network, API error) | Log `[WRN]`, fall back to `QuestionDetector` result — if `LooksLikeQuestion`, treat as `NewQuestion` |
| Haiku returns unparseable response | Treat as `NotAQuestion`, log `[DBG]` |
| Accumulator flush on session stop | `Flush()` drains buffer through full classification pipeline; if cancellation token fires during in-flight call, cancel cleanly — no turn created |

Worst case on Haiku failure is current behaviour — never silence.

---

## Section 4: Testing

### `SegmentAccumulator` unit tests (`AIHelperNET.Application.Tests`)
- Single segment flushes after 3s gap
- Two segments within 3s are combined into one string
- Third segment beyond 3s gap triggers flush of first two, starts new buffer
- `Flush()` on empty buffer returns null
- `Flush()` on non-empty buffer returns buffered text

### `QuestionDetector` pre-filter additions (existing test class)
- Hallucination phrase "software engineering, system design, coding" → rejected
- 3-word segment → rejected; mock classifier asserts it was never called

### `HaikuQuestionClassifier` unit tests (`AIHelperNET.Infrastructure.Tests`)
- Parses "NewQuestion" → `ClassificationResult.NewQuestion`
- Parses "Continuation" → `ClassificationResult.Continuation`
- Parses "NotAQuestion" → `ClassificationResult.NotAQuestion`
- Unparseable response → `ClassificationResult.NotAQuestion`
- Uses mock HTTP handler — no real Haiku calls in tests

### `TranscriptPipelineService` unit tests (new cases in `AIHelperNET.Application.Tests`)
- `NewQuestion` result → new turn created, `GenerateAnswerCommand` fired
- `Continuation` result with open turn → question text updated, answer re-generated
- `Continuation` result with no open turn → promoted to `NewQuestion`
- `NotAQuestion` → no turn created
- Haiku failure → falls back to `QuestionDetector` result

---

## Files Changed

| File | Change |
|---|---|
| `Application/Abstractions/IQuestionClassifier.cs` | New port interface + `ClassificationResult` enum |
| `Application/Sessions/SegmentAccumulator.cs` | New accumulator class |
| `Application/Sessions/TranscriptPipelineService.cs` | Wire accumulator + pre-filter + classifier; handle `Continuation` |
| `Domain/Questions/QuestionDetector.cs` | Add hallucination phrase blocklist entries |
| `Infrastructure/AI/HaikuQuestionClassifier.cs` | New Haiku HTTP adapter |
| `App/App.xaml.cs` | Register `IQuestionClassifier → HaikuQuestionClassifier` singleton in DI host |

---

## Out of Scope

- User-configurable gap threshold (hardcoded 3s constant in v1)
- Confidence score thresholding (Whisper.NET doesn't surface reliable per-segment probabilities)
- Any changes to `IAnswerProvider`, answer generation, or the OCR pipeline
- Language detection or multilingual support
