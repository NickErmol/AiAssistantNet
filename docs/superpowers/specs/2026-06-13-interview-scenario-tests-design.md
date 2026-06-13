# Interview Scenario Tests — Design

**Date:** 2026-06-13
**Branch:** `feature/auto-screen-mode-from-speech`
**Status:** Approved (design)

## Motivation

A manual QA document (`Test 1.docx`) describes real interview situations the app must handle:
an interviewer speaks an instruction ("write a SQL…"), sends a screen of DB tables that the app
captures, the candidate and interviewer exchange a few lines, and ~10 minutes later the interviewer
adds a requirement ("now select only distinct values"). The doc asks: *"What will be in the response
card?"*

We want a **bunch of automated xUnit tests in the same spirit** — encoding these scenarios (plus
.NET- and Angular-flavored ones) so the deterministic pipeline that runs *before* the LLM call is
pinned by tests. No live LLM is involved.

## Scope

For each scenario we assert the deterministic chain up to (but not including) the model call:

1. **`ScreenModeClassifier.Classify(interviewerSpeech)`** selects the correct `ScreenAnalysisMode`
   (or `null` for non-task chit-chat).
2. **`PromptBuilderService.BuildWithScreenMode(...)`** produces a well-formed screen-analysis prompt:
   - the captured OCR text appears in the user prompt, under the `On-screen content (OCR):` label;
   - the interviewer line appears in the user prompt;
   - the system prompt carries the mode-specific instruction;
   - candidate-stack lines appear in the system prompt when the `CodeProfile` is populated
     (the .NET / Angular scenarios);
   - `MaxTokens` honors the ≥2000 floor.
3. **`PromptBuilderService.BuildScreenFollowUp(...)`** for delayed follow-ups: the new requirement,
   the prior answer, and the original OCR are all present in the user prompt.

**Out of scope:** asserting the *content* of the generated answer card (requires the LLM); the audio
capture, OCR, and HTTP layers; UI.

## Design

### Fixture

A single new test file `tests/AIHelperNET.Application.Tests/Answers/InterviewScenarioTests.cs`
containing an inline record and a catalog:

```csharp
public sealed record InterviewScenario(
    string Name,
    string InterviewerSpeech,
    string ScreenOcr,
    ScreenAnalysisMode? ExpectedMode,   // null = no auto mode (chit-chat control)
    CodeProfile Profile,
    string? FollowUpSpeech = null,
    string? PriorAnswer = null);
```

A static `Catalog` exposes the scenarios; `[Theory]` + `[MemberData]` drive three test methods:

- `Classify_SelectsExpectedMode` — over all scenarios (asserts the mode, incl. `null` for chit-chat).
- `BuildWithScreenMode_IsWellFormed` — over scenarios with a non-null mode.
- `BuildScreenFollowUp_CarriesRequirementAndPriorAnswer` — over scenarios with a `FollowUpSpeech`.

This complements the existing unit tests (`ScreenModeClassifierTests`, `PromptBuilderServiceTests`)
rather than duplicating them: those test pieces in isolation; this tests realistic end-to-end
fixtures derived from actual interview phrasings.

### Scenario catalog (11)

**From `Test 1.docx`:**
1. **SQL – student location access** — *"write a SQL to get the students who has access to the
   location more than 2"* + `Student`/`Locations`/`StudentLocAccess` tables → `SolveCodingTask`;
   **follow-up** *"now select only distinct values"* with a representative prior answer.
2. **SQL – find the main manager** — *"write a SQL to find the main manager"* +
   `Employees`/`EmployeeManager` (circular `Y->X`) → `SolveCodingTask`.
3. **C# – super manager** — *"write a C# method to return the super manager"* +
   `Employees`/`EmployeeManagerRelation` (circular/break) → `SolveCodingTask`.

**.NET** (profile: C# / ASP.NET Core / EF Core):
4. **.NET coding** — *"implement a minimal API endpoint that…"* → `SolveCodingTask`.
5. **.NET debug** — *"can you fix the bug — why is this async method deadlocking"* → `DebugError`.
6. **.NET explain** — *"explain this code"* over a LINQ snippet → `ExplainCode`.
7. **.NET design** — *"how would you design this checkout service"* → `SystemDesign`.

**Angular** (profile: TypeScript / Angular / RxJS):
8. **Angular coding** — *"implement an Angular component that…"* → `SolveCodingTask`.
9. **Angular debug** — *"fix the bug in this RxJS subscription leak"* → `DebugError`.
10. **Angular explain** — *"explain this code"* over a change-detection snippet → `ExplainCode`.

**Negative control:**
11. **Chit-chat** — *"tell me about your experience with databases"* → `Classify` returns `null`.

### Classifier addition (approved)

Scenario 3's real phrasing **"write a C# method"** does not match the current `SolveCodingTask`
phrase list (it has `"write a method"`, not the `write … C# … method` pattern), so today it returns
`null`. We add two phrases — `"write a c# method"` and `"write c# method"` — to
`ScreenModeClassifier.cs` so the real docx phrasing routes to `SolveCodingTask`. TDD order: the
scenario test fails first, the phrase addition makes it pass. We also add a covering case to the
existing `ScreenModeClassifierTests`.

## Testing

`dotnet test tests/AIHelperNET.Application.Tests` — all green, including the new file and the
existing classifier/prompt suites. The classifier change ships with its unit-test coverage in the
same commit.

## Risks / notes

- The OCR fixtures are plain text transcriptions of the docx screenshots (the table contents), not
  the PNG images — that matches what the OCR layer feeds into `BuildWithScreenMode` at runtime.
- `MultipleChoice` is intentionally absent: the classifier has no MC rule (MC mode is selected
  another way), so no MC scenario is driven through `Classify`.
- Assertions match on stable prompt landmarks already relied on by `PromptBuilderServiceTests`
  (`On-screen content (OCR):`, the `do not pad` line, mode-prompt fragments) to avoid brittleness.
