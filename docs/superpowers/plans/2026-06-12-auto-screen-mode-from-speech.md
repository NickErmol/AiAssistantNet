# Auto-Select Screen Analysis Mode from Speech — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Auto-select `SessionControlViewModel.ScreenAnalysisMode` from the interviewer's spoken instruction ("write a SQL", "fix the bug", "explain this code", "design a system"), so a screen capture taken right after uses the right mode without a manual click.

**Architecture:** One new pure Application service, `ScreenModeClassifier`, maps interviewer text → `ScreenAnalysisMode?` by deterministic keyword matching (null = no signal). A thin `SessionControlViewModel.AutoSelectScreenMode` latch calls it and updates the mode only on a non-null result. The existing transcript-sink handler in `App.OnStartup` invokes the latch for each new interviewer line.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions, CommunityToolkit.Mvvm.

---

## File Structure

- **New** `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs` — pure static classifier (the only logic; fully tested).
- **New** `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs` — classifier unit tests.
- **Edit** `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` — add `AutoSelectScreenMode`.
- **Edit** `src/AIHelperNET.App/App.xaml.cs` — resolve `SessionControlViewModel` early; call the latch in the `Speaker.Other` branch.

---

### Task 1: `ScreenModeClassifier` (pure Application service, TDD)

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs`:

```csharp
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenModeClassifierTests
{
    [Theory]
    [InlineData("Can you write a SQL to get the students")]
    [InlineData("write a query that returns the top 5")]
    [InlineData("Now implement an LRU cache")]
    [InlineData("solve this task in 20 minutes")]
    [InlineData("write a function that reverses a string")]
    public void Classify_CodingPhrases_ReturnsSolveCodingTask(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.SolveCodingTask);

    [Theory]
    [InlineData("can you fix the bug in this method")]
    [InlineData("why is this failing when I run it")]
    [InlineData("what's wrong with this code")]
    public void Classify_DebugPhrases_ReturnsDebugError(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.DebugError);

    [Theory]
    [InlineData("explain this code to me")]
    [InlineData("what does this code do")]
    [InlineData("walk me through this code")]
    public void Classify_ExplainPhrases_ReturnsExplainCode(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.ExplainCode);

    [Theory]
    [InlineData("design a system for a URL shortener")]
    [InlineData("how would you design a rate limiter")]
    public void Classify_DesignPhrases_ReturnsSystemDesign(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.SystemDesign);

    [Fact]
    public void Classify_FixTheCode_PrefersDebugOverCoding()
        => ScreenModeClassifier.Classify("can you fix the code here")
            .Should().Be(ScreenAnalysisMode.DebugError);

    [Fact]
    public void Classify_ExplainTheCode_PrefersExplainOverCoding()
        => ScreenModeClassifier.Classify("explain the code on screen")
            .Should().Be(ScreenAnalysisMode.ExplainCode);

    [Fact]
    public void Classify_IsCaseInsensitive()
        => ScreenModeClassifier.Classify("WRITE A SQL QUERY")
            .Should().Be(ScreenAnalysisMode.SolveCodingTask);

    [Theory]
    [InlineData("tell me about your experience with databases")]
    [InlineData("what is a primary key")]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NonTaskOrEmpty_ReturnsNull(string text)
        => ScreenModeClassifier.Classify(text).Should().BeNull();
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenModeClassifier"`
Expected: FAIL to compile — `ScreenModeClassifier` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs`:

```csharp
using System;

namespace AIHelperNET.Application.Answers;

/// <summary>Maps interviewer speech to a <see cref="ScreenAnalysisMode"/> using deterministic
/// keyword matching, so a spoken instruction ("write a SQL", "fix the bug") pre-selects the
/// screen-analysis mode before the candidate captures. Pure — no I/O, no AI call.</summary>
public static class ScreenModeClassifier
{
    // Ordered most-specific first: "fix the code" / "explain the code" must win over the generic
    // coding bucket, so DebugError and ExplainCode are checked before SolveCodingTask.
    private static readonly (ScreenAnalysisMode Mode, string[] Phrases)[] Rules =
    [
        (ScreenAnalysisMode.DebugError, new[]
        {
            "fix the bug", "fix this bug", "fix the error", "fix this error", "fix the code",
            "fix this code", "debug this", "debug the", "what's wrong with", "what is wrong with",
            "why is this failing", "why does this fail", "why is it failing",
        }),
        (ScreenAnalysisMode.ExplainCode, new[]
        {
            "explain this code", "explain the code", "what does this code do", "what does this do",
            "walk me through this code", "walk me through the code",
        }),
        (ScreenAnalysisMode.SystemDesign, new[]
        {
            "design a system", "system design", "how would you design", "design the architecture",
            "architect this", "architect the",
        }),
        (ScreenAnalysisMode.SolveCodingTask, new[]
        {
            "write a sql", "write sql", "write a query", "write the query", "write a code",
            "write code", "write a function", "write a method", "write a script", "implement",
            "solve this task", "solve the task", "code this",
        }),
    ];

    /// <summary>Returns the matching mode on a confident keyword hit, or <see langword="null"/>
    /// when the text carries no clear screen-task instruction.</summary>
    /// <param name="interviewerText">The most recent interviewer transcript line.</param>
    public static ScreenAnalysisMode? Classify(string interviewerText)
    {
        if (string.IsNullOrWhiteSpace(interviewerText))
            return null;

        foreach (var (mode, phrases) in Rules)
            foreach (var phrase in phrases)
                if (interviewerText.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    return mode;

        return null;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenModeClassifier"`
Expected: PASS (all classifier tests green).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs
git commit -m "feat(answers): ScreenModeClassifier maps interviewer speech to ScreenAnalysisMode"
```

---

### Task 2: Wire the latch into the VM and app startup

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` (add method after `AutoSelectScreenMode`'s sibling members)
- Modify: `src/AIHelperNET.App/App.xaml.cs:62` (resolve `SessionControlViewModel` early) and `:72-79` (call latch in the `Other` branch) and `:185` (remove the now-duplicate declaration)

> No unit test — this is a 2-line latch over the tested classifier, and `SessionControlViewModel`'s constructor pulls in concrete heavy services (`SessionRunner` → `TranscriptPipelineService`) that aren't substitutable. Verified by build + manual smoke (see Manual Verification below).

- [ ] **Step 1: Add `AutoSelectScreenMode` to `SessionControlViewModel`**

In `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`, add this method to the class
(the file already has `using AIHelperNET.Application.Answers;` at the top, so `ScreenModeClassifier`
resolves). Place it directly after the `ActiveSessionId` property (around line 54):

```csharp
    /// <summary>Pre-selects <see cref="ScreenAnalysisMode"/> from the interviewer's latest line
    /// using <see cref="ScreenModeClassifier"/>. A confident match updates the mode (the overlay
    /// toggle reflects it); a non-match leaves the current selection untouched, so the mode latches
    /// to the last spoken or manually chosen value and never reverts to General on chit-chat.</summary>
    /// <param name="interviewerText">The most recent interviewer transcript line.</param>
    public void AutoSelectScreenMode(string interviewerText)
    {
        if (ScreenModeClassifier.Classify(interviewerText) is { } mode)
            ScreenAnalysisMode = mode;
    }
```

- [ ] **Step 2: Resolve `SessionControlViewModel` early in `App.OnStartup`**

In `src/AIHelperNET.App/App.xaml.cs`, immediately after the `turnVm` resolution (line 62), add:

```csharp
        // Resolve the SessionControlViewModel singleton early — its screen-analysis mode is
        // auto-selected from interviewer speech inside the transcript handler below.
        var sessionVm = _host.Services.GetRequiredService<SessionControlViewModel>();
```

- [ ] **Step 3: Call the latch in the `Speaker.Other` branch**

In the same file, inside the transcript handler's `Speaker.Other` block, after the existing
`turnVm.UpdateInterviewerLines(last5);` line (line 78), add:

```csharp
                // Pre-select the screen-analysis mode from what the interviewer just asked.
                sessionVm.AutoSelectScreenMode(item.Text);
```

- [ ] **Step 4: Remove the now-duplicate `sessionVm` declaration**

Further down in `App.OnStartup` (line 185), the hotkey wiring re-resolves the same singleton:

```csharp
        var sessionVm = _host.Services.GetRequiredService<SessionControlViewModel>();
```

Delete this line — `sessionVm` is already in scope from Step 2 (same singleton instance).
Leave the following `var turnVm2 = ...` line and all hotkey wiring unchanged.

- [ ] **Step 5: Build the App project to verify it compiles**

> If the overlay app is currently running it locks the output DLLs (MSB3027) — stop it first.

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings, 0 errors (warnings are errors in this repo).

- [ ] **Step 6: Run the full test suite**

Run: `dotnet test`
Expected: All green except the one known SQLite-shared-cache lock flake in Integration
(bug 6). No new failures.

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs src/AIHelperNET.App/App.xaml.cs
git commit -m "feat(app): auto-select screen analysis mode from interviewer speech"
```

---

## Manual Verification (after Task 2)

Use the `run-aihelper` skill to launch the overlay, then:

1. Start a session. With system audio capturing the interviewer, have them say (or play)
   "write a SQL query to get the students". → The **Solve coding task** toggle should light up
   automatically (mode latched).
2. Say "actually, can you fix the bug in it". → The **Debug error** toggle should take over.
3. Say a non-task line, e.g. "tell me about your experience". → The toggle should **not** change
   (no revert to General).
4. Manually click a different mode, then say a non-task line. → Manual choice sticks.

---

## Self-Review Notes

- **Spec coverage:** all four mode families (Task 1 `Rules`), latch-on-positive + never-reset
  (Task 2 `AutoSelectScreenMode`), wiring at the existing `Other` branch (Task 2), pure-classifier
  tests (Task 1). Manual smoke replaces the dropped VM unit test per the spec's testing section.
- **No placeholders:** every step has full code/commands.
- **Type consistency:** `ScreenModeClassifier.Classify` returns `ScreenAnalysisMode?`, consumed as
  `is { } mode` in `AutoSelectScreenMode`; both reference the existing
  `AIHelperNET.Application.Answers.ScreenAnalysisMode` enum.
