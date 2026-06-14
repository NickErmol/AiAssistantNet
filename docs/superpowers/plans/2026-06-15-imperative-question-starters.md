# Imperative Question Starters Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Recognize imperative/command-style and non-obvious requests (e.g. "Explain…", "List…", "Define recursion", "please explain…", "N+1 queries") as answerable tasks in the live interview pipeline.

**Architecture:** Two layers. (1) A deterministic heuristic layer (`QuestionBoundaryDetector` + `QuestionDetector`) gains an expanded, unified imperative lexicon, polite-prefix stripping, and a 2-word floor for imperatives. (2) The semantic layer (Haiku `QuestionBoundaryClassifier`) gets an enriched prompt for verb-less cases, and the heuristic's `<4-word → Unrelated @0.95` short-circuit is relaxed for topic-like fragments so they reach the AI (pipeline calls AI only when heuristic confidence `< 0.7`). Both phases ship in one PR; Phase 1 (Tasks 1-3) is a safe checkpoint, Phase 2 (Tasks 4-6) is eval-validated.

**Tech Stack:** .NET 10, C# latest, xUnit + FluentAssertions. Domain layer is platform-neutral. Spec: `docs/superpowers/specs/2026-06-15-imperative-question-starters-design.md`.

**Reference — current behavior to preserve:**
- Pipeline calls the AI classifier only when heuristic `result.Confidence < 0.7` (`TranscriptPipelineService.cs` ~line 197).
- `QuestionBoundaryDetector` rules fire in order; first match wins. Rule 2 = `<4 words → Unrelated @0.95`; Rule 3 = filler; Rule 10 = imperative-first-word + `≥4` words → `TaskComplete`.
- Domain has **no** `InternalsVisibleTo`, so `QuestionLexicon` is `internal` and is tested **through** the detectors' public `Evaluate`, not directly.

---

## File Structure

| File | Responsibility | Tasks |
|---|---|---|
| `src/AIHelperNET.Domain/Questions/QuestionLexicon.cs` (new) | Single source of truth: imperative verbs, multi-word imperative phrases, politeness prefixes, and the `StripPoliteness` / `StartsWithImperative` helpers | 1 |
| `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs` | Live-pipeline heuristic; consume lexicon, add politeness + 2-word imperative floor + Rule 2 exemption (P1) and topic-like relaxation (P2) | 1, 2, 4 |
| `src/AIHelperNET.Domain/Questions/QuestionDetector.cs` | Offline/dedup detector; consume lexicon + 2-word imperative floor | 1, 3 |
| `src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs` | Enrich Haiku `SystemPrompt` with verb-less semantic cases | 5 |
| `tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs` | New heuristic behavior tests | 2, 4 |
| `tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs` | New imperative-floor tests | 3 |

---

## Task 1: Unify imperative lexicon (refactor, no behavior change)

Create the shared `QuestionLexicon` and route both detectors through it. This is a pure refactor guarded by the **existing** test suite — no new tests, no behavior change yet.

**Files:**
- Create: `src/AIHelperNET.Domain/Questions/QuestionLexicon.cs`
- Modify: `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs` (remove local `Imperatives` set; reference lexicon)
- Modify: `src/AIHelperNET.Domain/Questions/QuestionDetector.cs` (remove local `ImperativeVerbs` set; reference lexicon)

- [ ] **Step 1: Create the lexicon file**

Create `src/AIHelperNET.Domain/Questions/QuestionLexicon.cs`:

```csharp
namespace AIHelperNET.Domain.Questions;

/// <summary>
/// Single source of truth for the lexical data used to detect imperative/command-style
/// requests. Shared by <see cref="QuestionBoundaryDetector"/> and <see cref="QuestionDetector"/>
/// so the two cannot drift apart.
/// </summary>
internal static class QuestionLexicon
{
    /// <summary>
    /// Imperative command verbs that, at the start of a segment, mark an answerable task.
    /// </summary>
    internal static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        // pre-existing
        "explain", "describe", "write", "implement", "design", "compare",
        "optimize", "refactor", "debug", "walk", "tell", "give", "show",
        "analyze", "fix", "build", "create", "outline", "discuss",
        // added for imperative-question-starters
        "provide", "list", "name", "define", "clarify", "summarize",
        "translate", "convert", "rewrite", "find", "generate", "review",
    };

    /// <summary>
    /// Multi-word imperative starters whose leading token is not a safe standalone verb
    /// (e.g. "break"). Matched as a whole-phrase prefix.
    /// </summary>
    internal static readonly string[] ImperativePhrases = ["break down"];

    /// <summary>
    /// Leading politeness markers stripped before first-word classification, longest first
    /// so "could you please" is consumed before "could you".
    /// </summary>
    private static readonly string[] PolitenessPrefixes =
    [
        "could you please", "can you please", "would you please",
        "could you", "can you", "would you", "please", "kindly",
    ];

    /// <summary>
    /// Removes a single leading politeness prefix (longest match first) and returns the
    /// remaining text trimmed. Returns the input unchanged when no prefix matches.
    /// </summary>
    internal static string StripPoliteness(string text)
    {
        var trimmed = text.TrimStart();
        foreach (var prefix in PolitenessPrefixes)
        {
            if (trimmed.Length > prefix.Length
                && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && IsBoundary(trimmed[prefix.Length]))
            {
                return trimmed[prefix.Length..].TrimStart(' ', ',');
            }
        }
        return text;
    }

    /// <summary>
    /// True when <paramref name="text"/>, after politeness stripping, begins with a known
    /// imperative verb or multi-word imperative phrase.
    /// </summary>
    internal static bool StartsWithImperative(string text)
    {
        var stripped = StripPoliteness(text);
        if (ImperativeVerbs.Contains(FirstWord(stripped)))
            return true;
        foreach (var phrase in ImperativePhrases)
        {
            if (stripped.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase)
                || stripped.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>The word count of <paramref name="text"/> after politeness stripping.</summary>
    internal static int StrippedWordCount(string text) =>
        StripPoliteness(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsBoundary(char c) => c is ' ' or ',';

    private static string FirstWord(string text)
    {
        var idx = text.IndexOf(' ');
        return (idx < 0 ? text : text[..idx]).Trim('?', '.', ',', '!');
    }
}
```

- [ ] **Step 2: Point `QuestionBoundaryDetector` at the lexicon**

In `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs`, DELETE the local `Imperatives` set (lines 21-26):

```csharp
    private static readonly HashSet<string> Imperatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain", "describe", "write", "implement", "design", "compare",
        "optimize", "refactor", "debug", "walk", "tell", "give", "show",
        "analyze", "fix", "build", "create", "outline", "discuss"
    };
```

Then replace the two remaining references to `Imperatives` with `QuestionLexicon.ImperativeVerbs`:
- Rule 7 (`Imperatives.Contains(firstWord)`) → `QuestionLexicon.ImperativeVerbs.Contains(firstWord)`
- Rule 10 (`if (Imperatives.Contains(firstWord) && words.Length >= 4)`) → `if (QuestionLexicon.ImperativeVerbs.Contains(firstWord) && words.Length >= 4)`
- Rule 9.5 (`Imperatives.Contains(words[1]...)`) → `QuestionLexicon.ImperativeVerbs.Contains(words[1]...)`

(Use Grep for `Imperatives` to confirm all references are updated; there should be exactly three after deleting the set.)

- [ ] **Step 3: Point `QuestionDetector` at the lexicon**

In `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`, DELETE the local `ImperativeVerbs` set (lines 15-19):

```csharp
    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain","describe","write","implement","design","compare","optimize",
        "refactor","debug","walk","tell","give","show"
    };
```

In `LooksLikeQuestion`, replace `ImperativeVerbs.Contains(first)` with `QuestionLexicon.ImperativeVerbs.Contains(first)`.

- [ ] **Step 4: Build and run the full Domain test suite (must stay green)**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj`
Expected: PASS, 0 failures. (Note: the verb set grew, but no rule yet uses the new verbs in a way the existing tests assert, so all existing tests remain green. `QuestionDetectorTests.Evaluate_QuestionText_DetectsAsQuestion` includes "Explain the SOLID principles" / "Design a rate limiter" which still pass.)

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Domain/Questions/QuestionLexicon.cs src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs src/AIHelperNET.Domain/Questions/QuestionDetector.cs
git commit -m "refactor(questions): unify imperative verb list into QuestionLexicon"
```

---

## Task 2: Expand recognition in QuestionBoundaryDetector (Phase 1)

Add the new verbs' behavior, short 2-word imperatives, `break down`, and polite prefixes — all in the **live-pipeline** detector. (Verbs are already in the lexicon from Task 1; this task wires the short-floor + politeness + Rule 2 exemption so they actually classify.)

**Files:**
- Modify: `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs`
- Test: `tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `QuestionBoundaryDetectorTests` (before the closing brace):

```csharp
    // ── Imperative-question-starters: new verbs at start → TaskComplete ─────────
    [Theory]
    [InlineData("List the SOLID principles for me")]
    [InlineData("Define the term idempotency clearly")]
    [InlineData("Summarize the actor model briefly")]
    [InlineData("Compare REST and gRPC tradeoffs")]
    [InlineData("Review this authentication approach")]
    [InlineData("Generate a regex for emails")]
    public void NewImperativeVerb_ReturnsTaskComplete(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
    }

    // ── Short (2-word) imperative command → TaskComplete (not Unrelated) ────────
    [Theory]
    [InlineData("Define recursion")]
    [InlineData("List exceptions")]
    [InlineData("Name three algorithms")]
    public void ShortImperative_ReturnsTaskComplete(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }

    // ── Multi-word imperative phrase "break down" → TaskComplete ────────────────
    [Fact]
    public void BreakDown_ReturnsTaskComplete()
    {
        var result = _sut.Evaluate(
            "Break down the authentication flow", Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }

    // ── "please"/"kindly" prefix stripped → underlying imperative → TaskComplete ─
    [Theory]
    [InlineData("Please explain the garbage collector")]
    [InlineData("Kindly summarize the CAP theorem")]
    [InlineData("Please define recursion")]
    public void PleaseImperative_ReturnsTaskComplete(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }

    // ── "could you"/"can you" forms generate an answer (label may be QuestionComplete
    //     via the interrogative Rule 9 when ≥6 words — both paths answer, which is the
    //     real requirement). ───────────────────────────────────────────────────────
    [Theory]
    [InlineData("Could you list the SOLID principles")]
    [InlineData("Can you describe the actor model")]
    public void CouldYouImperative_GeneratesAnswer(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.ShouldGenerateAnswer.Should().BeTrue();
        result.ShouldCreateNewTurn.Should().BeTrue();
    }

    // ── Negatives still filtered ────────────────────────────────────────────────
    [Theory]
    [InlineData("Got it")]
    [InlineData("Thanks")]
    [InlineData("Okay sounds good")]
    public void Acknowledgement_StaysUnrelated(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.Unrelated);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~QuestionBoundaryDetectorTests"`
Expected: FAIL on `NewImperativeVerb_*` (some pass via existing ≥4-word path, but `Review this authentication approach` etc. are fine; the short ones and polite ones FAIL — `Define recursion` returns `Unrelated` via Rule 2, `Please explain…` falls through to NoQuestion).

- [ ] **Step 3: Add the Rule 2 exemption**

In `QuestionBoundaryDetector.Evaluate`, replace the Rule 2 block:

```csharp
        // Rule 2: Word count < 4 → Unrelated
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4)
        {
            return Unrelated(normalized, 0.95, "Fewer than 4 words");
        }
```

with (exempt short imperatives so they reach Rule 10):

```csharp
        // Rule 2: Word count < 4 → Unrelated, UNLESS it begins with an imperative command
        // (e.g. "Define recursion") — those are short answerable tasks handled by Rule 10.
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 && !QuestionLexicon.StartsWithImperative(normalized))
        {
            return Unrelated(normalized, 0.95, "Fewer than 4 words");
        }
```

- [ ] **Step 4: Rewrite Rule 10 to use politeness-stripped imperative detection + 2-word floor**

Replace the Rule 10 block:

```csharp
        // Rule 10: TaskComplete — imperative first word with ≥4 words
        if (QuestionLexicon.ImperativeVerbs.Contains(firstWord) && words.Length >= 4)
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.TaskComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: "Imperative verb start with sufficient word count");
        }
```

with (verb **or** phrase, politeness-stripped, `≥2` words):

```csharp
        // Rule 10: TaskComplete — imperative command (verb or phrase, politeness-stripped)
        // with ≥2 words. The 2-word floor lets short commands ("Define recursion") through;
        // the interrogative gates above keep their ≥4/≥6 floors.
        if (QuestionLexicon.StartsWithImperative(normalized)
            && QuestionLexicon.StrippedWordCount(normalized) >= 2)
        {
            return new BoundaryClassificationResult(
                Classification: BoundaryLabel.TaskComplete,
                Confidence: 0.85,
                ShouldGenerateAnswer: true,
                ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true,
                NormalizedQuestionText: normalized,
                Reason: "Imperative command start with sufficient word count");
        }
```

- [ ] **Step 5: Run the new tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~QuestionBoundaryDetectorTests"`
Expected: PASS, 0 failures (new + all pre-existing boundary tests).

- [ ] **Step 6: Run the full Domain suite (guard against regressions)**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj`
Expected: PASS, 0 failures.

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs
git commit -m "feat(questions): recognize expanded + short + polite imperatives in boundary detector"
```

---

## Task 3: Expand recognition in QuestionDetector (Phase 1)

Mirror the 2-word imperative floor + politeness in the offline/dedup detector so its view stays consistent with the boundary detector.

**Files:**
- Modify: `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`
- Test: `tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `QuestionDetectorTests` (before the closing brace):

```csharp
    [Theory]
    [InlineData("Define recursion")]
    [InlineData("List exceptions")]
    [InlineData("Break down the auth flow")]
    [InlineData("Please explain the GC")]
    [InlineData("Summarize the CAP theorem")]
    public void Evaluate_ImperativeCommand_DetectsAsQuestion(string text)
    {
        var result = _sut.Evaluate(text, []);
        result.IsQuestion.Should().BeTrue();
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~QuestionDetectorTests"`
Expected: FAIL — `Define recursion` (2 words) is rejected by `MinWords = 4`; `Please explain…` first word is "please".

- [ ] **Step 3: Update `LooksLikeQuestion` for the imperative path**

In `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`, replace:

```csharp
    private static bool LooksLikeQuestion(string text)
    {
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
            return false;
        if (text.EndsWith('?')) return true;
        var first = FirstWord(text);
        return Interrogatives.Contains(first) || QuestionLexicon.ImperativeVerbs.Contains(first);
    }
```

with:

```csharp
    private static bool LooksLikeQuestion(string text)
    {
        var isImperative = QuestionLexicon.StartsWithImperative(text);
        // Imperative commands may be as short as 2 words ("Define recursion"); other text
        // keeps the MinWords floor.
        var minWords = isImperative ? 2 : MinWords;
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < minWords)
            return false;
        if (text.EndsWith('?')) return true;
        return Interrogatives.Contains(FirstWord(text)) || isImperative;
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~QuestionDetectorTests"`
Expected: PASS. (Pre-existing `Evaluate_FewerThan4Words_NotAQuestion` cases — "So.", "What?", "Go on.", "Can you" — still pass: none begin with an imperative verb, so the 2-word floor doesn't apply to them.)

- [ ] **Step 5: Run the full Domain suite**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj`
Expected: PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Domain/Questions/QuestionDetector.cs tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs
git commit -m "feat(questions): recognize short + polite imperatives in QuestionDetector"
```

---

## Task 4: Relax Rule 2 for topic-like fragments (Phase 2b)

Let short, verb-less technical topics ("N+1 queries", "Func vs Expression<Func>") fall below the `0.7` confidence threshold so the pipeline routes them to the AI classifier, while pure short filler keeps its high-confidence `Unrelated`.

**Files:**
- Modify: `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs`
- Test: `tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs`

- [ ] **Step 1: Write the failing tests**

Append to `QuestionBoundaryDetectorTests`:

```csharp
    // ── Phase 2b: short technical topic → low-confidence Unrelated (reaches AI) ─
    [Theory]
    [InlineData("N+1 queries")]
    [InlineData("Func vs Expression<Func>")]
    [InlineData("Change detection OnPush")]
    public void ShortTechnicalTopic_LowConfidenceUnrelated_ForAiClassifier(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.Unrelated);
        result.Confidence.Should().BeLessThan(0.7,
            "AI classifier must be invoked to judge whether a short technical topic implies a request");
    }

    // ── Phase 2b: plain short non-topic stays high-confidence Unrelated ─────────
    [Theory]
    [InlineData("What?")]
    [InlineData("The weather today")]
    public void ShortPlainPhrase_StaysHighConfidenceUnrelated(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecentQuestions);
        result.Classification.Should().Be(BoundaryLabel.Unrelated);
        result.Confidence.Should().BeGreaterThan(0.90);
    }
```

- [ ] **Step 2: Run the new tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~ShortTechnicalTopic"`
Expected: FAIL — "N+1 queries" currently returns `Unrelated @0.95`.

- [ ] **Step 3: Add the topic-like sniff and use it in Rule 2**

In `QuestionBoundaryDetector`, update the Rule 2 block (from Task 2) to:

```csharp
        // Rule 2: Word count < 4 → Unrelated, UNLESS it begins with an imperative command.
        // A short fragment that looks like a technical topic ("N+1 queries", "Func vs
        // Expression<Func>") may be an implicit "explain this" — emit low confidence so the
        // pipeline (confidence < 0.7) defers to the AI classifier.
        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 4 && !QuestionLexicon.StartsWithImperative(normalized))
        {
            return LooksLikeTechnicalTopic(normalized)
                ? Unrelated(normalized, 0.50, "Short technical topic — deferring to AI classifier")
                : Unrelated(normalized, 0.95, "Fewer than 4 words");
        }
```

Add this private helper next to `FirstWord` (near the bottom of the class):

```csharp
    /// <summary>
    /// Heuristic sniff for a short fragment that reads like a technical topic worth explaining:
    /// code punctuation, a "vs"/"versus" token, a mixed alphanumeric token ("N+1", "IPv4"),
    /// or an internal capital ("OnPush", "PascalCase"). Used only to LOWER confidence so the
    /// AI classifier is consulted — never to assert a question on its own.
    /// </summary>
    private static bool LooksLikeTechnicalTopic(string text)
    {
        if (text.IndexOfAny(['<', '>', '(', ')', '{', '}', '[', ']', ':', '+', '/', '#']) >= 0)
            return true;

        foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = token.Trim('.', ',', '?', '!');
            if (bare.Equals("vs", StringComparison.OrdinalIgnoreCase)
                || bare.Equals("versus", StringComparison.OrdinalIgnoreCase))
                return true;
            if (bare.Any(char.IsDigit) && bare.Any(char.IsLetter))
                return true;
            if (bare.Length > 1 && bare.Skip(1).Any(char.IsUpper))
                return true;
        }
        return false;
    }
```

- [ ] **Step 4: Run the new tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj --filter "FullyQualifiedName~QuestionBoundaryDetectorTests"`
Expected: PASS. Confirm the pre-existing `TooShort_ReturnsUnrelated` ("What?") still passes — "What?" has no topic signal, so it keeps `0.95`.

- [ ] **Step 5: Run the full Domain suite**

Run: `dotnet test tests/AIHelperNET.Domain.Tests/AIHelperNET.Domain.Tests.csproj`
Expected: PASS, 0 failures.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs
git commit -m "feat(questions): defer short technical topics to AI classifier (Rule 2 relaxation)"
```

---

## Task 5: Enrich the Haiku classifier prompt (Phase 2a)

Teach the LLM fallback the verb-less semantic cases (short technical topics, code-implies-explanation, follow-up refinement phrases). The prompt is a `private const string` and is not unit-testable in isolation — it is validated by the live eval in Task 6. Keep edits additive and JSON-shape-preserving.

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs` (the `SystemPrompt` const)

- [ ] **Step 1: Extend the label definitions**

In the `SystemPrompt`, find the `QuestionComplete` definition and append guidance for a bare topic. Replace:

```
        - QuestionComplete: text_to_classify is itself a complete, answerable QUESTION — a direct
          interrogative such as "what is dependency injection?", "how do the SOLID principles apply?",
          "when would you add an index?". A direct question is QuestionComplete, NOT QuestionStarted.
```

with:

```
        - QuestionComplete: text_to_classify is itself a complete, answerable QUESTION — a direct
          interrogative such as "what is dependency injection?", "how do the SOLID principles apply?",
          "when would you add an index?". A bare technical TOPIC stated as a prompt to explain it
          ("N+1 queries", "change detection and OnPush", "Func vs Expression<Func>") is also
          QuestionComplete — the interviewer is asking you to explain that topic. A direct question
          or a stated topic is QuestionComplete, NOT QuestionStarted.
```

- [ ] **Step 2: Add a follow-up refinement note to QuestionContinued**

Find the `QuestionContinued` definition and append after its existing text (before the next label line):

```
          Short refinement nudges from the interviewer on the SAME topic ("go deeper", "more
          detail", "with an example", "what about edge cases") are QuestionContinued (or
          AdditionalRequirement once answered), not a NewQuestion.
```

- [ ] **Step 3: Add examples for the new cases**

In the `Examples (input -> correct label)` block, add these lines after the existing `write a function…` example:

```
        - latest:"N+1 queries" status:null -> QuestionComplete (a bare technical topic stated as a request to explain it)
        - latest:"why does this code print 42" status:null -> QuestionComplete (code-grounded explain-this question)
        - recent:["explain the actor model"] latest(Other):"go a bit deeper" status:PreliminaryReady -> QuestionContinued (refinement nudge on the same topic)
```

- [ ] **Step 4: Build Infrastructure to confirm the string compiles**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings (raw-string literal indentation preserved).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Infrastructure/AI/QuestionBoundaryClassifier.cs
git commit -m "feat(questions): teach Haiku classifier verb-less topics and follow-up nudges"
```

---

## Task 6: Validate with the live boundary eval + full build (Phase 2 gate)

Confirm the prompt + Rule 2 changes don't regress the held-out classifier accuracy, and the whole solution builds and tests clean.

**Files:** none (validation only; commit any eval-corpus additions).

- [ ] **Step 1: Capture the baseline eval (before any further change is risky — run as-is now)**

Use the `run-ai-eval` skill (feeds the Anthropic key from Windows Credential Manager). Run the boundary classifier eval and record the held-out accuracy (expected floor: 12/12).

- [ ] **Step 2: (Optional) Add new-case rows to the eval corpus**

If adding coverage, append short-topic / code-implies / follow-up rows to `tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json` following the existing row shape. Re-run the eval; the floor must not drop.

- [ ] **Step 3: Full solution build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on). If the App is running and locks DLLs (MSB3027), stop it first (see `run-aihelper` skill).

- [ ] **Step 4: Full test suite**

Run: `dotnet test`
Expected: PASS, 0 failures across all projects.

- [ ] **Step 5: Commit any eval-corpus changes**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json
git commit -m "test(questions): add imperative/topic rows to boundary eval holdout"
```

(Skip if Step 2 was not done.)

---

## Self-Review

- **Spec coverage:**
  - Rubric rules 1 (interrogatives) — pre-existing, untouched. ✓
  - Rule 2 (request verbs incl. full requested list) — Task 1 (lexicon) + Task 2/3 (wiring). ✓
  - Rule 3 (polite prefixes) — `StripPoliteness` (Task 1) + Task 2/3 tests. ✓
  - Rule 4 (short technical topics) — Task 4 (Rule 2 relaxation) + Task 5 (prompt). ✓
  - Rule 5 (follow-up phrases) — Task 5 (prompt: QuestionContinued nudges). ✓
  - Rule 6 (code + implies) — Task 5 (prompt example). ✓
  - Negative (acknowledgements) — pre-existing filler guard, asserted in Task 2. ✓
  - Phase 1f Rule 2 exemption — Task 2 Step 3. ✓
  - Phase 2b topic sniff — Task 4. ✓
  - Phase 2c eval re-run — Task 6. ✓
  - 1a unify lists — Task 1. ✓  1c `break down` — Task 1 `ImperativePhrases` + Task 2 test. ✓
- **Placeholder scan:** none — every code step shows full code; eval step references the `run-ai-eval` skill (a real, committed skill).
- **Type consistency:** `QuestionLexicon.ImperativeVerbs`, `.ImperativePhrases`, `.StripPoliteness`, `.StartsWithImperative`, `.StrippedWordCount`, and `LooksLikeTechnicalTopic` are used with identical signatures across Tasks 1-4. ✓
```
