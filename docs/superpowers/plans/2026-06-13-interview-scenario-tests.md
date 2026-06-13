# Interview Scenario Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add automated xUnit scenario tests that encode the `Test 1.docx` interview situations
(plus .NET- and Angular-flavored ones) through the real `ScreenModeClassifier` +
`PromptBuilderService` chain, with no live LLM.

**Architecture:** One new test file holds an `InterviewScenario` record and an 11-scenario catalog;
three `[Theory]` methods drive it (mode classification, screen-mode prompt well-formedness, follow-up
prompt). A small, approved 2-phrase addition to `ScreenModeClassifier` makes the real docx phrasing
"write a C# method" route to `SolveCodingTask`.

**Tech Stack:** .NET 10, xUnit, FluentAssertions. Targets `tests/AIHelperNET.Application.Tests`
(no Windows target, runs headless).

---

## File Structure

- **Modify:** `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs` — add two phrases to the
  `SolveCodingTask` rule.
- **Modify:** `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs` — add a
  covering `[InlineData]` for "write a C# method".
- **Create:** `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs` — the
  `InterviewScenario` record, the `Catalog`, and three theory methods.

---

## Task 1: Route "write a C# method" to SolveCodingTask

**Files:**
- Modify: `tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs`
- Modify: `src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs:28-34`

- [ ] **Step 1: Add the failing test case**

In `ScreenModeClassifierTests.cs`, add one `[InlineData]` to the existing
`Classify_CodingPhrases_ReturnsSolveCodingTask` theory (after the "sequel" line, ~line 15):

```csharp
    [InlineData("write a C# method to return the super manager")] // real docx Task 3 phrasing
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Classify_CodingPhrases_ReturnsSolveCodingTask"`
Expected: FAIL — the new case returns `null` (no matching phrase), so `Should().Be(SolveCodingTask)` fails.

- [ ] **Step 3: Add the two phrases to the classifier**

In `ScreenModeClassifier.cs`, in the `SolveCodingTask` rule's phrase array (the block starting
`(ScreenAnalysisMode.SolveCodingTask, new[]` at line 28), add the two phrases. Place them right
after `"write a method",`:

```csharp
            "write a method", "write a c# method", "write c# method",
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Classify_CodingPhrases_ReturnsSolveCodingTask"`
Expected: PASS — all coding-phrase cases, including the new one, return `SolveCodingTask`.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenModeClassifier.cs tests/AIHelperNET.Application.Tests/Answers/ScreenModeClassifierTests.cs
git commit -m "feat(answers): ScreenModeClassifier routes 'write a C# method' to SolveCodingTask"
```

---

## Task 2: Scenario fixture, catalog, and classifier-mode theory

**Files:**
- Create: `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs`

- [ ] **Step 1: Create the file with the record, catalog, and the Classify theory**

Create `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs` with exactly this
content. The OCR strings are plain-text transcriptions of the docx screenshots; they use `\n`
escapes (normal string literals, NOT raw `"""` literals) so the newline is always LF and
`Contains` matches regardless of the source file's line endings.

```csharp
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>End-to-end-ish scenario tests derived from the manual QA doc (Test 1.docx): each
/// scenario is interviewer speech + a captured screen (OCR) + an optional delayed follow-up. They
/// pin the deterministic chain that runs before the LLM call — mode classification and prompt
/// assembly — without any live model.</summary>
public class InterviewScenarioTests
{
    /// <summary>One interview situation: what the interviewer said, what the screen showed, the
    /// mode it should select (null = no auto mode), the candidate stack in play, and an optional
    /// delayed follow-up with the prior answer it refines.</summary>
    public sealed record InterviewScenario(
        string Name,
        string InterviewerSpeech,
        string ScreenOcr,
        ScreenAnalysisMode? ExpectedMode,
        CodeProfile Profile,
        string? FollowUpSpeech = null,
        string? PriorAnswer = null);

    private static readonly CodeProfile DotNet = CodeProfile.Empty with
    {
        ProgrammingLanguage = "C#",
        BackendFramework = "ASP.NET Core",
        Database = "EF Core",
    };

    private static readonly CodeProfile Angular = CodeProfile.Empty with
    {
        ProgrammingLanguage = "TypeScript",
        FrontendFramework = "Angular",
        TestingFramework = "Jasmine",
    };

    // OCR transcriptions of the three docx screenshots.
    private const string SqlAccessOcr =
        "Student Table\nID Name Age\n1 A 20\n2 B 21\n3 C 23\n4 D 24\n" +
        "Locations\nId Name\n1 Library\n2 FoodCourt\n3 Auditorium\n" +
        "StudentLocAccess\nId StudId LocationId\n1 1 1\n2 1 2\n3 2 1\n4 3 1\n5 3 2\n6 3 3";

    private const string ManagerOcr =
        "Employees\nA B C D E X Y\n" +
        "EmployeeManager\nA->B\nB->C\nC->D\nX->E\nE->Y\nY->X";

    private const string SuperManagerOcr =
        "DB Table: Employees\nEmployeeId | FullName\nA | Adam\nB | Bryan\nC | Celie\nD | Dany\n" +
        "E | Edward\nX | Xing\nZ | Zang\n" +
        "DB Table: EmployeeManagerRelation\nEmployeeId | ManagerId\n" +
        "A | B\nB | C\nC | D\nX | E\nE | Y\nY | X  (circular/break)";

    public static readonly IReadOnlyList<InterviewScenario> Catalog =
    [
        // ── The three Test 1.docx scenarios ──────────────────────────────────────
        new("Sql_StudentLocationAccess",
            "write a SQL to get the students who has an access to the location more than 2",
            SqlAccessOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty,
            FollowUpSpeech: "now select only distinct values",
            PriorAnswer: "SELECT s.Name FROM Student s JOIN StudentLocAccess a ON a.StudId = s.ID " +
                         "GROUP BY s.ID, s.Name HAVING COUNT(a.LocationId) > 2;"),

        new("Sql_FindMainManager",
            "write a SQL to find the main manager",
            ManagerOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty),

        new("Csharp_SuperManager",
            "write a C# method to return the super manager",
            SuperManagerOcr,
            ScreenAnalysisMode.SolveCodingTask,
            CodeProfile.Empty),

        // ── .NET scenarios ───────────────────────────────────────────────────────
        new("DotNet_Coding",
            "implement a minimal API endpoint that returns an order by id",
            "public record Order(int Id, decimal Total);\n// GET /orders/{id} -> 404 when missing",
            ScreenAnalysisMode.SolveCodingTask,
            DotNet),

        new("DotNet_Debug",
            "can you fix the bug, why is this request deadlocking",
            "public string Get() => GetAsync().Result; // blocks on async in ASP.NET context",
            ScreenAnalysisMode.DebugError,
            DotNet),

        new("DotNet_Explain",
            "explain this code for me",
            "var top = orders.GroupBy(o => o.CustomerId)\n" +
            "    .Select(g => new { g.Key, Total = g.Sum(x => x.Total) })\n" +
            "    .OrderByDescending(t => t.Total).Take(3);",
            ScreenAnalysisMode.ExplainCode,
            DotNet),

        new("DotNet_Design",
            "how would you design this checkout service",
            "Requirements: checkout service, 10k rps, idempotent payments, audit trail",
            ScreenAnalysisMode.SystemDesign,
            DotNet),

        // ── Angular scenarios ────────────────────────────────────────────────────
        new("Angular_Coding",
            "implement an Angular counter component",
            "// CounterComponent: a count signal with increment() and decrement()",
            ScreenAnalysisMode.SolveCodingTask,
            Angular),

        new("Angular_Debug",
            "fix the bug in this RxJS subscription, it leaks",
            "ngOnInit() { this.svc.data$.subscribe(d => this.data = d); } // never unsubscribed",
            ScreenAnalysisMode.DebugError,
            Angular),

        new("Angular_Explain",
            "explain this code, what does OnPush do here",
            "@Component({ changeDetection: ChangeDetectionStrategy.OnPush })\n" +
            "export class ListComponent { @Input() items: Item[] = []; }",
            ScreenAnalysisMode.ExplainCode,
            Angular),

        // ── Negative control: chit-chat must not auto-select a mode ───────────────
        new("ChitChat_NoMode",
            "tell me about your experience with databases",
            string.Empty,
            ExpectedMode: null,
            CodeProfile.Empty),
    ];

    private static readonly Dictionary<string, InterviewScenario> ByName =
        Catalog.ToDictionary(s => s.Name);

    public static IEnumerable<object[]> AllScenarioNames =>
        Catalog.Select(s => new object[] { s.Name });

    public static IEnumerable<object[]> ModeScenarioNames =>
        Catalog.Where(s => s.ExpectedMode is not null).Select(s => new object[] { s.Name });

    public static IEnumerable<object[]> FollowUpScenarioNames =>
        Catalog.Where(s => s.FollowUpSpeech is not null).Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(AllScenarioNames))]
    public void Classify_SelectsExpectedMode(string name)
    {
        var s = ByName[name];
        ScreenModeClassifier.Classify(s.InterviewerSpeech).Should().Be(s.ExpectedMode);
    }
}
```

- [ ] **Step 2: Run the new theory to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~InterviewScenarioTests.Classify_SelectsExpectedMode"`
Expected: PASS — 11 cases. Each scenario's speech routes to its `ExpectedMode` (the chit-chat case
to `null`). This depends on Task 1 (the "write a C# method" phrase).

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs
git commit -m "test(answers): interview scenario catalog + classifier-mode theory"
```

---

## Task 3: BuildWithScreenMode well-formedness theory

**Files:**
- Modify: `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs`

- [ ] **Step 1: Add the mode-fragment helper and the well-formedness theory**

Add these members to the `InterviewScenarioTests` class (after `Classify_SelectsExpectedMode`). The
`ModeFragment` strings are verbatim substrings of the mode system prompts in
`PromptBuilderService.ModeSystemPrompt`:

```csharp
    private static string ModeFragment(ScreenAnalysisMode mode) => mode switch
    {
        ScreenAnalysisMode.SolveCodingTask => "provide the solution FIRST",
        ScreenAnalysisMode.DebugError      => "root cause and fix FIRST",
        ScreenAnalysisMode.ExplainCode     => "what the code does in one sentence FIRST",
        ScreenAnalysisMode.SystemDesign    => "recommended approach FIRST",
        _                                  => "senior software engineer",
    };

    [Theory]
    [MemberData(nameof(ModeScenarioNames))]
    public void BuildWithScreenMode_IsWellFormed(string name)
    {
        var s = ByName[name];
        var mode = s.ExpectedMode!.Value;

        var prompt = PromptBuilderService.BuildWithScreenMode(
            s.Profile,
            AnswerSettings.Default,
            s.ScreenOcr,
            new[] { s.InterviewerSpeech },
            mode);

        // Mode-specific instruction is in the system prompt.
        prompt.System.Should().Contain(ModeFragment(mode));

        // Captured screen is fenced as data under the OCR label, and is present verbatim.
        prompt.User.Should().Contain("On-screen content (OCR):");
        prompt.User.Should().Contain(s.ScreenOcr);

        // Interviewer instruction is carried into the prompt.
        prompt.User.Should().Contain(s.InterviewerSpeech);

        // Screen analysis floors output at 2000 tokens regardless of length setting.
        prompt.MaxTokens.Should().BeGreaterThanOrEqualTo(2000);

        // A populated candidate stack surfaces in the system prompt (the .NET / Angular scenarios).
        if (s.Profile != CodeProfile.Empty)
        {
            prompt.System.Should().Contain("Candidate stack (use this in code examples only):");
            prompt.System.Should().Contain(s.Profile.ProgrammingLanguage!);
        }
    }
```

- [ ] **Step 2: Run the theory to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~InterviewScenarioTests.BuildWithScreenMode_IsWellFormed"`
Expected: PASS — 10 cases (all but the chit-chat control).

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs
git commit -m "test(answers): assert BuildWithScreenMode prompt is well-formed per scenario"
```

---

## Task 4: BuildScreenFollowUp theory

**Files:**
- Modify: `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs`

- [ ] **Step 1: Add the follow-up theory**

Add this method to `InterviewScenarioTests` (after `BuildWithScreenMode_IsWellFormed`). It models
the docx's delayed *"now select only distinct values"* turn: the follow-up requirement plus the
prior answer plus the original OCR must all reach the prompt.

```csharp
    [Theory]
    [MemberData(nameof(FollowUpScenarioNames))]
    public void BuildScreenFollowUp_CarriesRequirementAndPriorAnswer(string name)
    {
        var s = ByName[name];

        var prompt = PromptBuilderService.BuildScreenFollowUp(
            s.Profile,
            AnswerSettings.Default,
            s.ScreenOcr,
            s.ExpectedMode!.Value,
            additions: new[] { s.FollowUpSpeech! },
            recentTranscript: Array.Empty<string>(),
            priorAnswer: s.PriorAnswer);

        prompt.User.Should().Contain("On-screen task (OCR):");
        prompt.User.Should().Contain(s.ScreenOcr);
        prompt.User.Should().Contain("Interviewer requirements (most recent last):");
        prompt.User.Should().Contain(s.FollowUpSpeech!);
        prompt.User.Should().Contain("Your previous answer:");
        prompt.User.Should().Contain(s.PriorAnswer!);
    }
```

- [ ] **Step 2: Run the theory to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~InterviewScenarioTests.BuildScreenFollowUp_CarriesRequirementAndPriorAnswer"`
Expected: PASS — 1 case (`Sql_StudentLocationAccess`).

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs
git commit -m "test(answers): assert screen follow-up carries requirement + prior answer"
```

---

## Task 5: Full suite verification

**Files:** none (verification only)

- [ ] **Step 1: Run the whole Application test project**

Run: `dotnet test tests/AIHelperNET.Application.Tests`
Expected: PASS — the full suite is green, including the existing `ScreenModeClassifierTests` /
`PromptBuilderServiceTests` and the new `InterviewScenarioTests` (22 new scenario assertions:
11 classify + 10 well-formed + 1 follow-up).

- [ ] **Step 2: Confirm no warnings broke the build**

Expected: build succeeds with `TreatWarningsAsErrors` — no analyzer/doc warnings introduced.
If a `[MemberData]` serialization warning appears, it is benign (the theory passes `string` names,
which serialize cleanly); no action needed.

---

## Self-Review Notes

- **Spec coverage:** All 11 scenarios from the spec are in `Catalog` (3 docx + 4 .NET + 3 Angular +
  1 chit-chat). The three assertion groups (classify / BuildWithScreenMode / BuildScreenFollowUp)
  map to the spec's three scope items. The approved classifier addition is Task 1.
- **Type consistency:** `InterviewScenario` fields, `ByName`, and the three `*ScenarioNames`
  member-data sources are referenced consistently across Tasks 2–4. `ModeFragment` covers every
  mode used by a scenario.
- **No placeholders:** every code/command step is complete and runnable.
