# Implicit Question Detection + Context-Aware Prompting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect "you tell me about…" style phrases as questions, and include recent transcript + Q&A history in every answer prompt so the AI has topic context.

**Architecture:** Two independent fixes — (1) a new Rule 9.5 in `QuestionBoundaryDetector` for indirect imperatives, and (2) optional `recentTranscript` / `recentQA` params added to `PromptBuilderService.Build()`, collected from the session in `GenerateAnswerHandler`.

**Tech Stack:** C# 13, .NET 10, xUnit, FluentAssertions, NSubstitute.

---

## File Map

| Action | File |
|---|---|
| Modify | `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs` |
| Modify | `tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs` |
| Modify | `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` |
| Modify | `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` |
| Modify | `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs` |

---

## Task 1: Failing tests for Rule 9.5 ("you [imperative]" patterns)

**Files:**
- Modify: `tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs`

- [ ] **Step 1: Add failing tests** — append these test methods to the existing `QuestionBoundaryDetectorTests` class (after the last existing test):

```csharp
// ── Rule 9.5: indirect imperative "you [verb]" ─────────────────────────────
[Fact]
public void YouTellMeAbout_FiveWords_ReturnsTaskComplete()
{
    var result = _sut.Evaluate(
        "You tell me about patterns",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().Be(BoundaryLabel.TaskComplete);
    result.ShouldGenerateAnswer.Should().BeTrue();
    result.ShouldCreateNewTurn.Should().BeTrue();
}

[Fact]
public void YouTellMeAbout_NineWords_ReturnsTaskComplete()
{
    var result = _sut.Evaluate(
        "You tell me about such patterns as fabric decorator builder",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().Be(BoundaryLabel.TaskComplete);
    result.ShouldGenerateAnswer.Should().BeTrue();
    result.ShouldCreateNewTurn.Should().BeTrue();
}

[Fact]
public void YouExplain_ReturnsTaskComplete()
{
    var result = _sut.Evaluate(
        "You explain how builder pattern works",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().Be(BoundaryLabel.TaskComplete);
    result.ShouldGenerateAnswer.Should().BeTrue();
}

[Fact]
public void YouAreRight_NotImperative_DoesNotReturnTaskComplete()
{
    // "are" is not in Imperatives — should NOT match Rule 9.5
    var result = _sut.Evaluate(
        "You are right about the factory pattern",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().NotBe(BoundaryLabel.TaskComplete);
}

[Fact]
public void YouKnow_NotImperative_DoesNotReturnTaskComplete()
{
    // "know" is not in Imperatives — should NOT match Rule 9.5
    var result = _sut.Evaluate(
        "You know what I mean about that",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().NotBe(BoundaryLabel.TaskComplete);
}

[Fact]
public void YouTell_OnlyFourWords_DoesNotReturnTaskComplete()
{
    // 4 words — falls below the ≥5 guard for Rule 9.5, hits Rule 2 (< 4) — wait,
    // 4 words passes Rule 2. Rule 9.5 requires ≥5 words.
    // "You tell me this" = 4 words → should not hit Rule 9.5.
    var result = _sut.Evaluate(
        "You tell me this",
        Speaker.Other, null, NoRecentQuestions);

    result.Classification.Should().NotBe(BoundaryLabel.TaskComplete);
}
```

- [ ] **Step 2: Run the new tests to confirm they fail**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "YouTellMeAbout_FiveWords|YouTellMeAbout_NineWords|YouExplain_Returns|YouAreRight|YouKnow_Not|YouTell_Only"
```

Expected: all 6 FAIL (rule not implemented yet).

---

## Task 2: Implement Rule 9.5 in QuestionBoundaryDetector

**Files:**
- Modify: `src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs`

- [ ] **Step 1: Insert Rule 9.5 between Rule 9 and Rule 10**

In `Evaluate()`, find the comment `// Rule 10: TaskComplete` and insert the following block immediately **before** it:

```csharp
// Rule 9.5: Indirect imperative — "you [imperative-verb] …" (≥5 words)
// Handles phrases like "You tell me about X", "You explain how Y works"
if (words.Length >= 5
    && words[0].Equals("you", StringComparison.OrdinalIgnoreCase)
    && Imperatives.Contains(words[1].ToLowerInvariant().Trim('.', '?', '!')))
{
    return new BoundaryClassificationResult(
        Classification: BoundaryLabel.TaskComplete,
        Confidence: 0.85,
        ShouldGenerateAnswer: true,
        ShouldRefineExistingAnswer: false,
        ShouldCreateNewTurn: true,
        NormalizedQuestionText: normalized,
        Reason: $"Indirect imperative 'you {words[1]}'");
}
```

- [ ] **Step 2: Run the new tests — all 6 should pass**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "YouTellMeAbout_FiveWords|YouTellMeAbout_NineWords|YouExplain_Returns|YouAreRight|YouKnow_Not|YouTell_Only"
```

Expected: all 6 PASS.

- [ ] **Step 3: Run the full Domain test suite to check for regressions**

```
dotnet test tests/AIHelperNET.Domain.Tests
```

Expected: all tests pass, 0 failures.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.Domain/Questions/QuestionBoundaryDetector.cs
git add tests/AIHelperNET.Domain.Tests/Questions/QuestionBoundaryDetectorTests.cs
git commit -m "feat: detect 'you [imperative]' phrases as TaskComplete (Rule 9.5)"
```

---

## Task 3: Failing tests for PromptBuilderService context parameters

**Files:**
- Modify: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`

- [ ] **Step 1: Add the required using at the top of the file** (if not already present):

```csharp
using AIHelperNET.Domain.Ids;
```

- [ ] **Step 2: Append failing tests** to the existing `PromptBuilderServiceTests` class:

```csharp
// ── Context-aware prompting ────────────────────────────────────────────────
[Fact]
public void Build_WithNoContext_UserContainsOnlyQuestion()
{
    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What is the pattern?");

    prompt.User.Should().Contain("Question: What is the pattern?");
    prompt.User.Should().NotContain("Conversation context");
}

[Fact]
public void Build_WithTranscriptContext_InjectsTranscriptBlock()
{
    var items = new List<TranscriptItem>
    {
        TranscriptItem.Create(Speaker.Other, "You tell me about fabric decorator builder", Now, 0.9f),
        TranscriptItem.Create(Speaker.Me,    "What is the pattern?", Now.AddSeconds(10), 0.9f),
    };

    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What is the pattern?",
        recentTranscript: items);

    prompt.User.Should().Contain("Conversation context");
    prompt.User.Should().Contain("[Transcript] Interviewer: You tell me about fabric decorator builder");
    prompt.User.Should().Contain("[Transcript] Me: What is the pattern?");
    prompt.User.Should().Contain("Question: What is the pattern?");
}

[Fact]
public void Build_WithQAContext_InjectsQABlock()
{
    var qa = new List<(string Question, string Answer)>
    {
        ("What is a design pattern?", "A design pattern is a reusable solution to a common problem."),
    };

    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What is the pattern?",
        recentQA: qa);

    prompt.User.Should().Contain("Conversation context");
    prompt.User.Should().Contain("[Q&A] Q: What is a design pattern?");
    prompt.User.Should().Contain("A design pattern is a reusable solution");
}

[Fact]
public void Build_WithBothContextTypes_InjectsBothBlocks()
{
    var items = new List<TranscriptItem>
    {
        TranscriptItem.Create(Speaker.Other, "You tell me about patterns", Now, 0.9f),
    };
    var qa = new List<(string Question, string Answer)>
    {
        ("What is OOP?", "OOP stands for Object-Oriented Programming."),
    };

    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What is the pattern?",
        recentTranscript: items,
        recentQA: qa);

    prompt.User.Should().Contain("[Transcript] Interviewer: You tell me about patterns");
    prompt.User.Should().Contain("[Q&A] Q: What is OOP?");
    prompt.User.Should().Contain("Question: What is the pattern?");
}

[Fact]
public void Build_QAAnswerLongerThan200Chars_IsTruncated()
{
    var longAnswer = new string('A', 250);
    var qa = new List<(string Question, string Answer)>
    {
        ("Short question?", longAnswer),
    };

    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What is the pattern?",
        recentQA: qa);

    // The answer in the prompt should be capped at 200 chars + ellipsis
    prompt.User.Should().NotContain(longAnswer);       // full 250-char string absent
    prompt.User.Should().Contain(new string('A', 200)); // first 200 chars present
}

[Fact]
public void Build_WithScreenContextAndTranscript_BothAppearInUser()
{
    var items = new List<TranscriptItem>
    {
        TranscriptItem.Create(Speaker.Other, "Explain this code", Now, 0.9f),
    };

    var prompt = PromptBuilderService.Build(
        CodeProfile.Empty, AnswerSettings.Default,
        "What does this do?",
        screenContext: "void Main() { }",
        recentTranscript: items);

    prompt.User.Should().Contain("[Transcript] Interviewer: Explain this code");
    prompt.User.Should().Contain("void Main()");
    prompt.User.Should().Contain("Question: What does this do?");
}
```

- [ ] **Step 3: Run the new tests to confirm they fail**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "WithNoContext|WithTranscriptContext|WithQAContext|WithBothContext|QAAnswerLonger|WithScreenContextAndTranscript"
```

Expected: all 6 FAIL (parameters don't exist yet).

---

## Task 4: Implement context parameters in PromptBuilderService

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`

- [ ] **Step 1: Add the `using` for `TranscriptItem`** at the top of `PromptBuilderService.cs` if not already present — the type is in `AIHelperNET.Domain.Sessions` which is already referenced by the `using AIHelperNET.Domain.Sessions;` line.

Confirm the file already has:
```csharp
using AIHelperNET.Domain.Sessions;
```

- [ ] **Step 2: Replace the `Build(CodeProfile, AnswerSettings, string, string?)` overload** with the extended signature. The full new method (replace lines 25–68 of the current file):

```csharp
/// <summary>Constructs an <see cref="AnswerPrompt"/> using an explicit question text.</summary>
/// <param name="profile">Candidate's code profile used to tailor code examples.</param>
/// <param name="settings">Answer settings controlling complexity, language, and length.</param>
/// <param name="questionText">The full question text to answer.</param>
/// <param name="screenContext">Optional OCR text captured from the screen.</param>
/// <param name="recentTranscript">Optional recent transcript items to include as conversation context.</param>
/// <param name="recentQA">Optional recent Q&amp;A pairs to include as conversation context. Answers are capped at 200 characters.</param>
public static AnswerPrompt Build(
    CodeProfile profile,
    AnswerSettings settings,
    string questionText,
    string? screenContext = null,
    IReadOnlyList<TranscriptItem>? recentTranscript = null,
    IReadOnlyList<(string Question, string Answer)>? recentQA = null)
{
    var system = new StringBuilder();

    system.AppendLine(
        "You are a senior software engineer coaching a candidate through a technical job interview. " +
        "Your job is to give short, spoken-style answers the candidate can say out loud right now.");

    system.AppendLine();
    system.AppendLine("STRICT RULES:");
    system.AppendLine("1. Be concise. 3–5 sentences or 3–4 bullets max. No padding.");
    system.AppendLine("2. Answer like an experienced engineer speaking — clear, direct, no filler.");
    system.AppendLine("3. NO markdown headers (no #, ##). Use plain prose or short bullets.");
    system.AppendLine("4. CODE: include code ONLY when the question explicitly asks to write, " +
        "implement, fix, debug, show syntax, or provide a query/example. " +
        "For conceptual, design, 'what is', 'why', 'how does it work' questions — verbal answer only.");
    system.AppendLine("5. Start directly with the answer. Never say 'Great question' or restate the question.");

    AppendCodeProfile(system, profile);

    if (settings.Complexity != AnswerComplexity.Balanced)
        system.AppendLine(CultureInfo.InvariantCulture,
            $"\nTarget level: {settings.Complexity}.");

    if (!string.IsNullOrWhiteSpace(settings.OutputLanguage) &&
        !settings.OutputLanguage.Equals("English", StringComparison.OrdinalIgnoreCase))
        system.AppendLine(CultureInfo.InvariantCulture,
            $"Answer in: {settings.OutputLanguage}.");

    var user = new StringBuilder();

    var hasTranscript = recentTranscript is { Count: > 0 };
    var hasQA         = recentQA         is { Count: > 0 };

    if (hasTranscript || hasQA)
    {
        user.AppendLine("Conversation context (recent discussion):");

        if (hasTranscript)
        {
            foreach (var item in recentTranscript!)
            {
                var speaker = item.Speaker == Speaker.Me ? "Me" : "Interviewer";
                user.AppendLine(CultureInfo.InvariantCulture,
                    $"[Transcript] {speaker}: {item.Text}");
            }
        }

        if (hasQA)
        {
            foreach (var (q, a) in recentQA!)
            {
                var cappedAnswer = a.Length > 200 ? a[..200] + "…" : a;
                user.AppendLine(CultureInfo.InvariantCulture,
                    $"[Q&A] Q: {q}  A: {cappedAnswer}");
            }
        }

        user.AppendLine();
    }

    user.AppendLine(CultureInfo.InvariantCulture, $"Question: {questionText}");
    if (!string.IsNullOrWhiteSpace(screenContext))
        user.AppendLine(CultureInfo.InvariantCulture, $"\nOn-screen context (OCR):\n{screenContext}");

    return new AnswerPrompt(
        System: system.ToString(),
        User: user.ToString(),
        OutputLanguage: settings.OutputLanguage,
        MaxTokens: MapLengthToTokens(settings.Length));
}
```

The `DetectedQuestion` overload on line 13 (`Build(profile, settings, question.Text, screenContext)`) still compiles unchanged — `screenContext` is the 4th positional arg in both old and new signatures.

- [ ] **Step 3: Run the failing tests — all 6 should now pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "WithNoContext|WithTranscriptContext|WithQAContext|WithBothContext|QAAnswerLonger|WithScreenContextAndTranscript"
```

Expected: all 6 PASS.

- [ ] **Step 4: Run the full Application test suite**

```
dotnet test tests/AIHelperNET.Application.Tests
```

Expected: all tests pass.

- [ ] **Step 5: Build the full solution**

```
dotnet build
```

Expected: Build succeeded, 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs
git add tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
git commit -m "feat: add recentTranscript and recentQA context to PromptBuilderService.Build()"
```

---

## Task 5: Wire context collection into GenerateAnswerHandler

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`

- [ ] **Step 1: Locate the context injection point** in `GenerateAnswerHandler.Handle()`

The current code (around line 48–62) looks like:

```csharp
var questionText = string.IsNullOrWhiteSpace(turn.InitialQuestionText)
    ? question.Text
    : turn.InitialQuestionText;

var genStatus = ...
...
var prompt = PromptBuilderService.Build(
    session.CodeProfile, session.AnswerSettings, questionText, cmd.ScreenContext);
```

- [ ] **Step 2: Insert context collection and pass to Build()** — replace the `questionText` resolution block through the `PromptBuilderService.Build(...)` call with:

```csharp
var questionText = string.IsNullOrWhiteSpace(turn.InitialQuestionText)
    ? question.Text
    : turn.InitialQuestionText;

// Collect last 5 transcript items up to (and including) when this turn was created.
var recentTranscript = session.Transcript
    .Where(t => t.Timestamp <= turn.CreatedAt)
    .TakeLast(5)
    .ToList();

// Collect last 2 completed turns (excluding the current one) with at least one answer version.
var recentQA = session.ConversationTurns
    .Where(t => t.Id != cmd.TurnId
             && t.AnswerVersions.Count > 0
             && (t.Status == ConversationTurnStatus.PreliminaryReady
                 || t.Status == ConversationTurnStatus.RefinedReady
                 || t.Status == ConversationTurnStatus.Resolved))
    .TakeLast(2)
    .Select(t =>
    {
        var ans = t.AnswerVersions[^1].Text;
        return (Question: t.InitialQuestionText,
                Answer: ans.Length > 200 ? ans[..200] + "…" : ans);
    })
    .ToList();

var genStatus = cmd.VersionType == AnswerVersionType.Preliminary
    ? ConversationTurnStatus.GeneratingPreliminary
    : ConversationTurnStatus.GeneratingRefined;
turn.TransitionTo(genStatus);

var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
if (start.IsFailed) return Result.Fail(start.Error);
var answer = start.Value;

var prompt = PromptBuilderService.Build(
    session.CodeProfile, session.AnswerSettings, questionText,
    cmd.ScreenContext,
    recentTranscript,
    recentQA);
```

- [ ] **Step 3: Verify the file compiles**

```
dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full test suite**

```
dotnet test
```

Expected: all tests pass (the handler change is pure wiring — no new unit tests needed; covered by E2E).

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs
git commit -m "feat: inject recent transcript and Q&A context into answer prompts"
```

---

## Task 6: Create feature branch and open PR

- [ ] **Step 1: Create a feature branch from develop**

```
git checkout develop
git checkout -b feature/implicit-question-context
git cherry-pick <commit-hash-task2> <commit-hash-task4> <commit-hash-task5>
```

Alternatively, if you have been working directly on `develop`, just push and branch from the latest HEAD. Ask the user which working style they prefer.

> **Preferred flow** (if not already on a feature branch): all commits above were made on `develop` locally. To create the PR cleanly, `git checkout -b feature/implicit-question-context` now (it branches from the current HEAD), then `git push -u origin feature/implicit-question-context`.

- [ ] **Step 2: Push the feature branch**

```
git push -u origin feature/implicit-question-context
```

- [ ] **Step 3: Open the PR targeting `develop`**

```
gh pr create --base develop \
  --title "feat: implicit question detection and context-aware prompting" \
  --body "..."
```

PR body should cover: the two bugs fixed, examples of before/after transcript behaviour, test summary, and a link to the spec doc.

---

## Self-Review Notes

- `session.Transcript` is the correct property name (not `TranscriptItems`) — confirmed from `Session.cs` line 34.
- `ConversationTurn.AnswerVersions` is `IReadOnlyList<AnswerVersion>` with oldest-first ordering — `[^1]` gives the most recent version.
- `AnswerVersion.Text` is the correct property name (confirmed from the `record` definition).
- `ConversationTurn.CreatedAt` is `DateTimeOffset` — safe for `<=` comparison with `TranscriptItem.Timestamp`.
- The `DetectedQuestion` overload of `Build()` (`Build(profile, settings, question, screenContext)`) still compiles unchanged — `screenContext` remains the 4th positional parameter in the new signature.
- `"can"`, `"could"`, `"would"`, `"will"` + `"you"` patterns already fire via Rule 9 (`Interrogatives.Contains(firstWord) && words.Length >= 6`) — no additional rule needed for those.
- The 4-word guard test (`YouTell_OnlyFourWords_DoesNotReturnTaskComplete`) is correct: "You tell me this" is 4 words. Rule 9.5 requires ≥5, so it falls through to Rule 12 (low-confidence NoQuestion). Rule 10 doesn't fire because `"you"` is not in `Imperatives`. ✓
