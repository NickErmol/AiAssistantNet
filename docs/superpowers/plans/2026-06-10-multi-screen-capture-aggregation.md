# Multi-Screen-Capture Aggregation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Aggregate several screen captures of one coding question (taken < 8s apart) into a single answer card over their combined OCR, regenerating on each capture, and raise the screen-capture token floor to 1000.

**Architecture:** A pure `ScreenCaptureAccumulator` (Application, unit-tested) owns the OCR buffer + gap-based grouping. The first capture of a group creates the card via a new fast `CreateScreenTurnCommand` (returns the turn id, no streaming); every capture then streams over the combined OCR via the existing cancellable `RegenerateAnswerWithScreenCommand`. The VM owns the accumulator, the group's turn id, and a `CancellationTokenSource` that cancels the in-flight generation on each new capture.

**Tech Stack:** .NET 10, C#, WPF, CommunityToolkit.Mvvm, Mediator, FluentResults, xUnit + FluentAssertions + NSubstitute. No new dependency. No Domain entity/EF change — **no migration**.

**Spec:** `docs/superpowers/specs/2026-06-10-multi-screen-capture-aggregation-design.md`

---

## File Structure

**Create:**
- `src/AIHelperNET.Application/Answers/ScreenCaptureAccumulator.cs` — pure buffer + grouping + combine.
- `src/AIHelperNET.Application/Answers/Commands/CreateScreenTurnCommand.cs` — create-turn-only command + handler.
- `tests/AIHelperNET.Application.Tests/Answers/ScreenCaptureAccumulatorTests.cs`
- `tests/AIHelperNET.Application.Tests/Answers/CreateScreenTurnHandlerTests.cs`

**Modify:**
- `src/AIHelperNET.Application/Answers/PromptBuilderService.cs:167` — token floor 500 → 1000.
- `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` — screen-mode floor tests.
- `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` — VM orchestration, `TimeProvider` ctor param, `TurnVm.InitialQuestion` observable, Dismiss/Resolve reset.

**Delete:**
- `src/AIHelperNET.Application/Answers/Commands/StartScreenTurnCommand.cs` — retired (only caller is the rewritten VM).

---

## Task 1: Raise the screen-capture token floor to 1000

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs:167`
- Test: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`

- [ ] **Step 1: Write the failing tests** (append inside the `PromptBuilderServiceTests` class)

```csharp
    [Theory]
    [InlineData(AnswerLength.VeryShort)]
    [InlineData(AnswerLength.ShortLength)]
    public void BuildWithScreenMode_AppliesThousandTokenFloor(AnswerLength length)
    {
        var settings = AnswerSettings.Default with { Length = length };
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, settings, "code on screen", new[] { "line" }, ScreenAnalysisMode.SolveCodingTask);
        prompt.MaxTokens.Should().Be(1000);
    }

    [Fact]
    public void BuildWithScreenMode_DeepDive_KeepsHigherMapping()
    {
        var settings = AnswerSettings.Default with { Length = AnswerLength.DeepDive };
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, settings, "code on screen", new[] { "line" }, ScreenAnalysisMode.SolveCodingTask);
        prompt.MaxTokens.Should().Be(2000);
    }
```

- [ ] **Step 2: Run to verify the floor test fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BuildWithScreenMode"`
Expected: `BuildWithScreenMode_AppliesThousandTokenFloor` FAILS (currently floored at 500, so VeryShort/Short → 500, not 1000). `DeepDive` passes (2000 unchanged).

- [ ] **Step 3: Change the floor**

In `PromptBuilderService.cs`, in `BuildWithScreenMode`, change:

```csharp
            MaxTokens: Math.Max(MapLengthToTokens(settings.Length), 500));
```
to:
```csharp
            MaxTokens: Math.Max(MapLengthToTokens(settings.Length), 1000));
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BuildWithScreenMode"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
git commit -m "feat(prompt): raise screen-capture token floor 500 -> 1000"
```

---

## Task 2: Pure `ScreenCaptureAccumulator`

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenCaptureAccumulator.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/ScreenCaptureAccumulatorTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AIHelperNET.Application.Tests/Answers/ScreenCaptureAccumulatorTests.cs`:

```csharp
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenCaptureAccumulatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(8);

    [Fact]
    public void Add_FirstCapture_IsNewGroup_RawOcr()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        var r = acc.Add("screen one", T0);
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("screen one");
    }

    [Fact]
    public void Add_SecondCaptureWithinGap_ContinuesGroup_LabeledConcat()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        var r = acc.Add("second", T0.AddSeconds(3));
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(2);
        r.CombinedOcr.Should().Be("--- Screen 1 ---\nfirst\n\n--- Screen 2 ---\nsecond");
    }

    [Fact]
    public void Add_CaptureAtGapBoundary_StartsNewGroup()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        var r = acc.Add("second", T0.AddSeconds(8)); // exactly gap → new group
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("second");
    }

    [Fact]
    public void Add_ThreeWithinGap_AccumulatesAllInOrder()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("a", T0);
        acc.Add("b", T0.AddSeconds(2));
        var r = acc.Add("c", T0.AddSeconds(4));
        r.IsNewGroup.Should().BeFalse();
        r.Count.Should().Be(3);
        r.CombinedOcr.Should().Be("--- Screen 1 ---\na\n\n--- Screen 2 ---\nb\n\n--- Screen 3 ---\nc");
    }

    [Fact]
    public void Reset_StartsFreshGroupOnNextAdd()
    {
        var acc = new ScreenCaptureAccumulator(Gap);
        acc.Add("first", T0);
        acc.Reset();
        var r = acc.Add("second", T0.AddSeconds(1)); // within gap, but reset → new group
        r.IsNewGroup.Should().BeTrue();
        r.Count.Should().Be(1);
        r.CombinedOcr.Should().Be("second");
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenCaptureAccumulator"`
Expected: FAIL — `ScreenCaptureAccumulator` does not exist (compile error).

- [ ] **Step 3: Implement the accumulator**

`src/AIHelperNET.Application/Answers/ScreenCaptureAccumulator.cs`:

```csharp
using System.Text;

namespace AIHelperNET.Application.Answers;

/// <summary>Result of adding a capture to a <see cref="ScreenCaptureAccumulator"/>.</summary>
/// <param name="IsNewGroup">True if this capture started a new group (caller should create a new card).</param>
/// <param name="CombinedOcr">All captures in the current group, labeled and concatenated.</param>
/// <param name="Count">Number of captures in the current group.</param>
public sealed record ScreenCaptureAddResult(bool IsNewGroup, string CombinedOcr, int Count);

/// <summary>
/// Groups consecutive screen captures by recency: captures less than <c>gap</c> apart belong to
/// one task and are combined; a longer gap (or <see cref="Reset"/>) starts a new group. Pure — the
/// caller supplies the timestamp.
/// </summary>
public sealed class ScreenCaptureAccumulator(TimeSpan gap)
{
    private readonly List<string> _captures = [];
    private DateTimeOffset _lastCaptureAt;
    private bool _hasGroup;

    /// <summary>Adds a capture's OCR and returns the resulting group state.</summary>
    public ScreenCaptureAddResult Add(string ocr, DateTimeOffset now)
    {
        var isNewGroup = !_hasGroup || (now - _lastCaptureAt) >= gap;
        if (isNewGroup)
            _captures.Clear();

        _captures.Add(ocr);
        _lastCaptureAt = now;
        _hasGroup = true;

        return new ScreenCaptureAddResult(isNewGroup, Combine(_captures), _captures.Count);
    }

    /// <summary>Ends the current group; the next <see cref="Add"/> starts fresh.</summary>
    public void Reset()
    {
        _captures.Clear();
        _hasGroup = false;
    }

    private static string Combine(IReadOnlyList<string> captures)
    {
        if (captures.Count == 1)
            return captures[0];

        var sb = new StringBuilder();
        for (var i = 0; i < captures.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            sb.Append("--- Screen ").Append(i + 1).Append(" ---\n").Append(captures[i]);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenCaptureAccumulator"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenCaptureAccumulator.cs tests/AIHelperNET.Application.Tests/Answers/ScreenCaptureAccumulatorTests.cs
git commit -m "feat(screen): pure ScreenCaptureAccumulator (gap grouping + labeled combine)"
```

---

## Task 3: `CreateScreenTurnCommand` (create card, return id, no streaming)

**Files:**
- Create: `src/AIHelperNET.Application/Answers/Commands/CreateScreenTurnCommand.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/CreateScreenTurnHandlerTests.cs`

Context: mirrors the turn-creation half of the existing `StartScreenTurnHandler` (read it for reference) but returns the new `ConversationTurnId` and does NOT stream. `Session.AddConversationTurn` returns a `DomainResult<ConversationTurn>` (`.IsFailed`, `.Error`, `.Value`); `repository.GetAsync` returns `Result<Session>`. `TransitionTo` only blocks terminal statuses, so the freshly created turn (status `Detected`) is later validly transitioned by `RegenerateAnswerWithScreenHandler`.

- [ ] **Step 1: Write the failing handler tests**

`tests/AIHelperNET.Application.Tests/Answers/CreateScreenTurnHandlerTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class CreateScreenTurnHandlerTests
{
    private static Session NewSession(FakeTimeProvider clock)
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, clock.GetUtcNow()).Value;

    [Fact]
    public async Task Handle_CreatesScreenTurn_NotifiesUi_ReturnsTurnId()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var session = NewSession(clock);
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Result.Ok(session));
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var sink = Substitute.For<IConversationTurnSink>();

        var handler = new CreateScreenTurnHandler(repo, sink, uow, clock);
        var result = await handler.Handle(new CreateScreenTurnCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().ContainSingle().Which.Id.Should().Be(result.Value);
        sink.Received(1).OnTurnCreated(result.Value, "[Screen capture]");
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersistenceFails_ReturnsFailure()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var session = NewSession(clock);
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Result.Ok(session));
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Fail("db error"));
        var sink = Substitute.For<IConversationTurnSink>();

        var handler = new CreateScreenTurnHandler(repo, sink, uow, clock);
        var result = await handler.Handle(new CreateScreenTurnCommand(session.Id), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~CreateScreenTurnHandler"`
Expected: FAIL — `CreateScreenTurnCommand`/`CreateScreenTurnHandler` do not exist.

- [ ] **Step 3: Implement the command + handler**

`src/AIHelperNET.Application/Answers/Commands/CreateScreenTurnCommand.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Creates an empty "[Screen capture]" conversation turn and returns its id (no streaming).</summary>
/// <param name="SessionId">The active session.</param>
public sealed record CreateScreenTurnCommand(SessionId SessionId) : IRequest<Result<ConversationTurnId>>;

/// <summary>Handles <see cref="CreateScreenTurnCommand"/>.</summary>
public sealed class CreateScreenTurnHandler(
    ISessionRepository repository,
    IConversationTurnSink turnSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<CreateScreenTurnCommand, Result<ConversationTurnId>>
{
    private const string QuestionLabel = "[Screen capture]";

    /// <inheritdoc/>
    public async ValueTask<Result<ConversationTurnId>> Handle(CreateScreenTurnCommand request, CancellationToken cancellationToken)
    {
        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return Result.Fail<ConversationTurnId>(get.Errors);
        var session = get.Value;

        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(QuestionLabel, QuestionSource.Ocr, now);
        session.AddDetectedQuestion(question);

        var turnResult = session.AddConversationTurn(question.Id, QuestionLabel, now);
        if (turnResult.IsFailed) return Result.Fail<ConversationTurnId>(turnResult.Error);
        var turn = turnResult.Value;

        // Notify UI so the card exists before the first answer chunk arrives.
        turnSink.OnTurnCreated(turn.Id, QuestionLabel);

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return Result.Fail<ConversationTurnId>(save.Errors);

        return Result.Ok(turn.Id);
    }
}
```

Note: if `turnResult.Error` is a `string`, `Result.Fail<ConversationTurnId>(turnResult.Error)` compiles. If the domain exposes errors differently, mirror exactly how `StartScreenTurnHandler` converts it (it uses `Result.Fail(turnResult.Error)`), just with the `<ConversationTurnId>` generic.

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~CreateScreenTurnHandler"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Commands/CreateScreenTurnCommand.cs tests/AIHelperNET.Application.Tests/Answers/CreateScreenTurnHandlerTests.cs
git commit -m "feat(screen): CreateScreenTurnCommand (create card, return id, no streaming)"
```

---

## Task 4: VM orchestration — accumulate, cancel, create-then-regenerate

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- Delete: `src/AIHelperNET.Application/Answers/Commands/StartScreenTurnCommand.cs`

No unit test (App-layer VM with timers/cancellation — established convention is FlaUI/manual; verified in Task 5). Read the current `CaptureScreenAsync`, `DismissAsync`, `ResolveAsync`, and the `TurnVm` class before editing.

- [ ] **Step 1: Add `TimeProvider` to the VM constructor**

Change the class declaration:
```csharp
public sealed partial class ConversationTurnViewModel(IMediator mediator) : ObservableObject
```
to:
```csharp
public sealed partial class ConversationTurnViewModel(IMediator mediator, TimeProvider clock) : ObservableObject
```
(`TimeProvider.System` is already registered as a singleton in `HostConfiguration.cs`; the VM is a singleton — it resolves.)

- [ ] **Step 2: Make `TurnVm.InitialQuestion` an observable settable property**

In `TurnVm` (currently `public string InitialQuestion => initialQuestion;`), replace that property with:
```csharp
    private string _initialQuestion = initialQuestion;
    /// <summary>Gets or sets the text of the initial question (updated for screen-capture count).</summary>
    public string InitialQuestion
    {
        get => _initialQuestion;
        set => SetProperty(ref _initialQuestion, value);
    }
```

- [ ] **Step 3: Add screen-group state fields to `ConversationTurnViewModel`**

Add near the top of `ConversationTurnViewModel` (after the `[ObservableProperty]` fields):
```csharp
    private static readonly TimeSpan ScreenCaptureGroupGap = TimeSpan.FromSeconds(8);
    private readonly ScreenCaptureAccumulator _screenAccumulator = new(ScreenCaptureGroupGap);
    private ConversationTurnId? _screenGroupTurnId;
    private CancellationTokenSource? _screenGenCts;
```
Add the using if missing: `using AIHelperNET.Application.Answers;`

- [ ] **Step 4: Rewrite `CaptureScreenAsync`**

Replace the entire current `CaptureScreenAsync` method body with:

```csharp
    /// <summary>Captures the screen and (re)generates one answer over the accumulated captures of the
    /// current task. Captures less than <see cref="ScreenCaptureGroupGap"/> apart refine the same card;
    /// a longer gap starts a new card. Each capture cancels any in-flight screen generation.</summary>
    [RelayCommand]
    private async Task CaptureScreenAsync(SessionControlViewModel? sessionControl)
    {
        if (ActiveSessionId is not { } sid) return;
        if (sessionControl is null) return;

        var ocrResult = await mediator.Send(new CaptureScreenCommand());
        if (ocrResult.IsFailed) return;

        string[] interviewerLines = sessionControl.IncludeInterviewerContext
            ? _lastInterviewerLines
            : [];

        var add = _screenAccumulator.Add(ocrResult.Value, clock.GetUtcNow());

        // Supersede any in-flight screen generation for this group.
        _screenGenCts?.Cancel();
        _screenGenCts?.Dispose();
        _screenGenCts = new CancellationTokenSource();
        var token = _screenGenCts.Token;

        if (add.IsNewGroup)
            _screenGroupTurnId = null;

        try
        {
            if (_screenGroupTurnId is null)
            {
                var created = await mediator.Send(new CreateScreenTurnCommand(sid), token);
                if (created.IsFailed) return;
                _screenGroupTurnId = created.Value;
            }

            var turnId = _screenGroupTurnId.Value;
            var turn = Turns.FirstOrDefault(t => t.Id == turnId);
            if (turn is not null)
                turn.InitialQuestion = add.Count > 1
                    ? $"[Screen capture · {add.Count} screens]"
                    : "[Screen capture]";

            await mediator.Send(new RegenerateAnswerWithScreenCommand(
                sid, turnId, add.CombinedOcr, sessionControl.ScreenAnalysisMode, interviewerLines), token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer capture — the next capture's generation takes over.
        }
    }
```

- [ ] **Step 5: Reset the accumulator on Dismiss/Resolve**

In `DismissAsync`, after `await mediator.Send(new DismissTurnCommand(sid, turn.Id));` and before `Turns.Remove(turn);`, add:
```csharp
        if (_screenGroupTurnId == turn.Id) { _screenAccumulator.Reset(); _screenGroupTurnId = null; }
```
In `ResolveAsync`, after `await mediator.Send(new ResolveTurnCommand(sid, turn.Id));` and before `Turns.Remove(turn);`, add the same line.

- [ ] **Step 6: Delete the retired command**

```bash
git rm src/AIHelperNET.Application/Answers/Commands/StartScreenTurnCommand.cs
```
(The rewritten `CaptureScreenAsync` no longer references it; it had no other callers or tests.)

- [ ] **Step 7: Build the whole solution**

Run: `dotnet build`
Expected: success, 0 warnings (TreatWarningsAsErrors). If `StartScreenTurnCommand` is still referenced anywhere, the build will name the file — fix that reference.

- [ ] **Step 8: Run the Application tests**

Run: `dotnet test tests/AIHelperNET.Application.Tests`
Expected: PASS (accumulator + handler + prompt floor tests included).

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat(screen): aggregate multi-capture into one card via accumulator + cancellable regenerate"
```

---

## Task 5: Full build, test, live visual verification

**Files:** none (verification only).

- [ ] **Step 1: Full build**

Run: `dotnet build`
Expected: 0 warnings / 0 errors.

- [ ] **Step 2: Core test suites**

Run each: `dotnet test tests/AIHelperNET.Domain.Tests`, `dotnet test tests/AIHelperNET.Application.Tests`, `dotnet test tests/AIHelperNET.Infrastructure.Tests`
Expected: all green.

- [ ] **Step 3: Launch and verify (run-aihelper skill)**

With an API key configured and a session running, exercise the screen-capture flow and confirm:
- Take 2–3 captures in quick succession (< 8s apart) of a (scrolled) coding task → **one card**; its answer reflects content from **all** captures; the label shows `[Screen capture · N screens]`; the card refines (new version) on each capture rather than spawning new cards.
- Wait > 8s, capture again → a **new card** (new task).
- With an audio turn present, a screen capture creates/uses its **own** screen card and does not hijack the audio turn.
- Rapid captures: the earlier (superseded) generations stop without showing an error card; the final answer covers all captures.

> If behavior is wrong: grouping/combine bugs → add a failing `ScreenCaptureAccumulatorTests` case first, then fix the accumulator; turn-targeting/cancellation bugs → fix `CaptureScreenAsync`. Rebuild and re-verify.

- [ ] **Step 4: Final commit (if fixes were needed)**

```bash
git add -A
git commit -m "fix(screen): adjustments from live multi-capture verification"
```

---

## Done

When all tasks are checked: multiple captures < 8s apart produce one card answered over their combined OCR (regenerating per capture, refining the same card), a gap starts a new card, screen captures no longer hijack audio turns, and screen answers get a ≥1000-token budget. Hand off to `superpowers:finishing-a-development-branch` to open the PR to `develop`.
