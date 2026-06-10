# Screen-Task Interviewer Follow-ups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** After a screen-capture answer card exists, interviewer audio that adds a condition to or asks about the captured task automatically produces a **new** card answered with full captured-task context (OCR + accumulated additions + prior answer) — the capture card is never mutated.

**Architecture:** A new in-memory `ScreenTaskContextStore` singleton bridges the VM's screen-capture path to the audio pipeline (which cannot see screen turns through the shared `Session`). The VM registers the captured task; `TranscriptPipelineService` detects interviewer follow-ups against it, accumulates additions, and (debounced) fires a new `GenerateScreenFollowUpCommand` that creates a fresh card and streams a context-aware answer. No DB schema change, no EF migration.

**Tech Stack:** .NET 10, C# latest, Mediator (source-gen CQRS), FluentResults, xUnit + FluentAssertions + NSubstitute, `Microsoft.Extensions.Time.Testing.FakeTimeProvider`.

**Spec:** `docs/superpowers/specs/2026-06-10-screen-task-interviewer-followup-design.md`

**Repo conventions:** `dotnet build` and `dotnet test` are warnings-as-errors; XML docs required on public members. End every commit message with the trailer:
`Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`

---

## File map

- **Create** `src/AIHelperNET.Application/Answers/ScreenTopic.cs` — pure topic-label derivation from OCR.
- **Create** `src/AIHelperNET.Application/Answers/ScreenTaskContextStore.cs` — `ScreenTaskContext` record + thread-safe singleton store.
- **Create** `src/AIHelperNET.Application/Answers/ScreenFollowUpRouter.cs` — `ScreenFollowUpOutcome` enum + pure `BoundaryLabel`→outcome map.
- **Create** `src/AIHelperNET.Application/Answers/Commands/GenerateScreenFollowUpCommand.cs` — command + handler.
- **Modify** `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs` — add `ScreenFollowUp`.
- **Modify** `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` — add `BuildScreenFollowUp`.
- **Modify** `src/AIHelperNET.Application/DependencyInjection.cs` — register `ScreenTaskContextStore`.
- **Modify** `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` — follow-up branch + helpers + reset.
- **Modify** `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` — inject store; `Register` after capture.
- **Tests** under `tests/AIHelperNET.Application.Tests/Answers/` and `.../Sessions/`.

---

### Task 1: Add `ScreenFollowUp` answer-version type

**Files:**
- Modify: `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs`

- [ ] **Step 1: Add the enum member**

In `AnswerVersionType.cs`, add after `FollowUp`:

```csharp
    /// <summary>Continuation of a previous answer with user-supplied follow-up text.</summary>
    FollowUp,
    /// <summary>Answer to an interviewer follow-up about a captured screen task (new card).</summary>
    ScreenFollowUp
```

- [ ] **Step 2: Build**

Run: `dotnet build src/AIHelperNET.Domain/AIHelperNET.Domain.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs
git commit -m "feat(domain): add ScreenFollowUp answer-version type"
```

---

### Task 2: `ScreenTopic` — derive a one-line task label from OCR

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenTopic.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/ScreenTopicTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenTopicTests
{
    [Fact]
    public void Derive_UsesFirstNonEmptyLine_Trimmed()
    {
        var ocr = "\n   Implement an LRU cache   \nwith O(1) get and put\n";
        ScreenTopic.Derive(ocr).Should().Be("Implement an LRU cache");
    }

    [Fact]
    public void Derive_CapsLongLineAt120Chars()
    {
        var ocr = new string('x', 200);
        ScreenTopic.Derive(ocr).Should().HaveLength(120);
    }

    [Fact]
    public void Derive_EmptyOrWhitespace_ReturnsFallback()
    {
        ScreenTopic.Derive("   ").Should().Be("Screen task");
        ScreenTopic.Derive("").Should().Be("Screen task");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenTopicTests"`
Expected: FAIL — `ScreenTopic` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

```csharp
namespace AIHelperNET.Application.Answers;

/// <summary>Derives a short, human-readable label for a captured screen task from its OCR text.</summary>
public static class ScreenTopic
{
    private const int MaxLength = 120;

    /// <summary>Returns the first non-empty OCR line trimmed to <see cref="MaxLength"/> chars,
    /// or <c>"Screen task"</c> when the OCR has no usable text.</summary>
    /// <param name="ocr">The captured OCR text.</param>
    public static string Derive(string ocr)
    {
        if (!string.IsNullOrWhiteSpace(ocr))
        {
            foreach (var raw in ocr.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                return line.Length > MaxLength ? line[..MaxLength] : line;
            }
        }
        return "Screen task";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenTopicTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenTopic.cs tests/AIHelperNET.Application.Tests/Answers/ScreenTopicTests.cs
git commit -m "feat(app): ScreenTopic — derive task label from OCR"
```

---

### Task 3: `ScreenTaskContextStore` — the in-memory bridge state

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenTaskContextStore.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/ScreenTaskContextStoreTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenTaskContextStoreTests
{
    private static ConversationTurnId NewId() => ConversationTurnId.New();

    [Fact]
    public void Register_NewGroup_SetsContextWithDerivedTopicAndEmptyAdditions()
    {
        var store = new ScreenTaskContextStore();
        var card = NewId();

        store.Register(card, "Implement LRU cache", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        store.Current.Should().NotBeNull();
        store.Current!.ScreenCardId.Should().Be(card);
        store.Current.LatestCardId.Should().Be(card);
        store.Current.TopicLabel.Should().Be("Implement LRU cache");
        store.Current.Additions.Should().BeEmpty();
    }

    [Fact]
    public void Register_SameGroup_UpdatesOcrButKeepsAdditions()
    {
        var store = new ScreenTaskContextStore();
        var card = NewId();
        store.Register(card, "task v1", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);
        store.AddAddition("make it thread-safe");

        store.Register(card, "task v1 + v2", ScreenAnalysisMode.SolveCodingTask, isNewGroup: false);

        store.Current!.Ocr.Should().Be("task v1 + v2");
        store.Current.Additions.Should().ContainSingle().Which.Should().Be("make it thread-safe");
    }

    [Fact]
    public void AddAddition_AccumulatesInOrder_AndCapsAtEight()
    {
        var store = new ScreenTaskContextStore();
        store.Register(NewId(), "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        for (var i = 1; i <= 10; i++) store.AddAddition($"cond{i}");

        store.Current!.Additions.Should().HaveCount(8);
        store.Current.Additions.First().Should().Be("cond3");   // oldest two dropped
        store.Current.Additions.Last().Should().Be("cond10");
    }

    [Fact]
    public void SetLatestCard_UpdatesParentPointer()
    {
        var store = new ScreenTaskContextStore();
        var a = NewId();
        store.Register(a, "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);
        var b = NewId();

        store.SetLatestCard(b);

        store.Current!.LatestCardId.Should().Be(b);
        store.Current.ScreenCardId.Should().Be(a, "the anchor card id is unchanged");
    }

    [Fact]
    public void Clear_RemovesContext()
    {
        var store = new ScreenTaskContextStore();
        store.Register(NewId(), "task", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        store.Clear();

        store.Current.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenTaskContextStoreTests"`
Expected: FAIL — `ScreenTaskContextStore` does not exist. (`ConversationTurnId.New()` is confirmed to exist in `src/AIHelperNET.Domain/Ids/ConversationTurnId.cs`.)

- [ ] **Step 3: Write minimal implementation**

```csharp
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Answers;

/// <summary>Immutable snapshot of the captured screen task currently in focus.</summary>
/// <param name="ScreenCardId">The original capture card (lineage anchor).</param>
/// <param name="TopicLabel">One-line task label (card title + classifier context).</param>
/// <param name="Ocr">Combined OCR of the captured task.</param>
/// <param name="Mode">The screen analysis mode the capture used.</param>
/// <param name="Additions">Accumulated interviewer additions (oldest → newest).</param>
/// <param name="LatestCardId">Most recent card in the lineage — the parent for the next follow-up.</param>
public sealed record ScreenTaskContext(
    ConversationTurnId ScreenCardId,
    string TopicLabel,
    string Ocr,
    ScreenAnalysisMode Mode,
    IReadOnlyList<string> Additions,
    ConversationTurnId LatestCardId);

/// <summary>
/// Thread-safe, in-memory record of the captured screen task in focus. Written by the VM's capture
/// path (<see cref="Register"/>) and the follow-up handler (<see cref="SetLatestCard"/>); read and
/// accumulated by the transcript pipeline. Per-session — cleared on session start.
/// </summary>
public sealed class ScreenTaskContextStore
{
    private const int MaxAdditions = 8;
    private readonly object _gate = new();
    private ScreenTaskContext? _current;

    /// <summary>The current screen task snapshot, or <see langword="null"/> if none is in focus.</summary>
    public ScreenTaskContext? Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>Registers (new task) or refreshes (same card, updated OCR) the screen task in focus.</summary>
    /// <param name="cardId">The capture card id.</param>
    /// <param name="ocr">Combined OCR of the captured task.</param>
    /// <param name="mode">The screen analysis mode used.</param>
    /// <param name="isNewGroup">True when this capture starts a fresh task (resets additions).</param>
    public void Register(ConversationTurnId cardId, string ocr, ScreenAnalysisMode mode, bool isNewGroup)
    {
        lock (_gate)
        {
            if (!isNewGroup && _current is not null && _current.ScreenCardId == cardId)
            {
                _current = _current with { Ocr = ocr, Mode = mode };
                return;
            }
            _current = new ScreenTaskContext(cardId, ScreenTopic.Derive(ocr), ocr, mode, [], cardId);
        }
    }

    /// <summary>Appends an interviewer addition, keeping at most the most recent eight.</summary>
    /// <param name="text">The interviewer utterance to accumulate.</param>
    public void AddAddition(string text)
    {
        lock (_gate)
        {
            if (_current is null) return;
            var list = _current.Additions.Append(text).ToList();
            if (list.Count > MaxAdditions) list = list.GetRange(list.Count - MaxAdditions, MaxAdditions);
            _current = _current with { Additions = list };
        }
    }

    /// <summary>Points the lineage at the newest follow-up card (the next follow-up's parent).</summary>
    /// <param name="id">The new card id.</param>
    public void SetLatestCard(ConversationTurnId id)
    {
        lock (_gate)
        {
            if (_current is not null) _current = _current with { LatestCardId = id };
        }
    }

    /// <summary>Drops the screen task in focus (interviewer moved on, or session reset).</summary>
    public void Clear()
    {
        lock (_gate) { _current = null; }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenTaskContextStoreTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenTaskContextStore.cs tests/AIHelperNET.Application.Tests/Answers/ScreenTaskContextStoreTests.cs
git commit -m "feat(app): ScreenTaskContextStore — in-memory screen-task bridge"
```

---

### Task 4: `ScreenFollowUpRouter` — map a boundary label to an outcome

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenFollowUpRouter.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/ScreenFollowUpRouterTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenFollowUpRouterTests
{
    [Theory]
    [InlineData(BoundaryLabel.AdditionalRequirement,          ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion, ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.QuestionContinued,              ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.QuestionComplete,               ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.TaskComplete,                   ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.NewQuestion,                    ScreenFollowUpOutcome.MovedOn)]
    [InlineData(BoundaryLabel.NoQuestion,                     ScreenFollowUpOutcome.Noise)]
    [InlineData(BoundaryLabel.Unrelated,                      ScreenFollowUpOutcome.Noise)]
    [InlineData(BoundaryLabel.QuestionStarted,                ScreenFollowUpOutcome.Noise)]
    public void Map_MapsLabelToOutcome(BoundaryLabel label, ScreenFollowUpOutcome expected)
        => ScreenFollowUpRouter.Map(label).Should().Be(expected);
}
```

> The `BoundaryLabel` members above are confirmed against
> `src/AIHelperNET.Domain/Questions/BoundaryLabel.cs` (`NoQuestion`, `QuestionStarted`,
> `QuestionContinued`, `QuestionComplete`, `TaskComplete`, `ClarificationOfCurrentQuestion`,
> `AdditionalRequirement`, `NewQuestion`, `Unrelated`).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenFollowUpRouterTests"`
Expected: FAIL — `ScreenFollowUpRouter` / `ScreenFollowUpOutcome` do not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Answers;

/// <summary>The action to take for interviewer speech while a captured screen task is in focus.</summary>
public enum ScreenFollowUpOutcome
{
    /// <summary>Speech adds to or asks about the captured task — spawn a new context-aware card.</summary>
    FollowUp,
    /// <summary>Interviewer started a new, unrelated question — drop the screen-task linkage.</summary>
    MovedOn,
    /// <summary>Noise / no question — ignore.</summary>
    Noise
}

/// <summary>Maps an AI boundary label to a <see cref="ScreenFollowUpOutcome"/>. Biased toward keeping
/// the captured task in context: only an explicit new-topic label ends the linkage.</summary>
public static class ScreenFollowUpRouter
{
    /// <summary>Maps <paramref name="label"/> to the screen follow-up action.</summary>
    public static ScreenFollowUpOutcome Map(BoundaryLabel label) => label switch
    {
        BoundaryLabel.AdditionalRequirement
            or BoundaryLabel.ClarificationOfCurrentQuestion
            or BoundaryLabel.QuestionContinued
            or BoundaryLabel.QuestionComplete
            or BoundaryLabel.TaskComplete => ScreenFollowUpOutcome.FollowUp,
        BoundaryLabel.NewQuestion => ScreenFollowUpOutcome.MovedOn,
        _ => ScreenFollowUpOutcome.Noise
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ScreenFollowUpRouterTests"`
Expected: PASS (9 cases).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/ScreenFollowUpRouter.cs tests/AIHelperNET.Application.Tests/Answers/ScreenFollowUpRouterTests.cs
git commit -m "feat(app): ScreenFollowUpRouter — boundary label to follow-up outcome"
```

---

### Task 5: `PromptBuilderService.BuildScreenFollowUp`

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderScreenFollowUpTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class PromptBuilderScreenFollowUpTests
{
    [Fact]
    public void BuildScreenFollowUp_FencesContext_Accumulates_AndKeeps2000Floor()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            screenContext: "Implement an LRU cache",
            mode: ScreenAnalysisMode.SolveCodingTask,
            additions: new[] { "make it thread-safe", "handle nulls" },
            recentTranscript: new[] { "Interviewer: and handle nulls" },
            priorAnswer: "class LruCache { }");

        prompt.System.Should().Contain("If they added conditions");          // decision instruction
        prompt.User.Should().Contain("On-screen task (OCR):");
        prompt.User.Should().Contain("Implement an LRU cache");
        prompt.User.Should().Contain("1. make it thread-safe");
        prompt.User.Should().Contain("2. handle nulls");                     // accumulation, ordered
        prompt.User.Should().Contain("Recent conversation:");
        prompt.User.Should().Contain("Your previous answer:");
        prompt.MaxTokens.Should().BeGreaterThanOrEqualTo(2000);
    }

    [Fact]
    public void BuildScreenFollowUp_OmitsPriorAnswerSection_WhenNull()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default, "task",
            ScreenAnalysisMode.SolveCodingTask,
            additions: new[] { "do X" }, recentTranscript: Array.Empty<string>(), priorAnswer: null);

        prompt.User.Should().NotContain("Your previous answer:");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~PromptBuilderScreenFollowUpTests"`
Expected: FAIL — `BuildScreenFollowUp` not defined.

- [ ] **Step 3: Write minimal implementation**

In `PromptBuilderService.cs`, add this method after `BuildWithScreenMode` (it reuses the existing
private `ModeSystemPrompt`, `AppendCodeProfile`, `SharedMarkdownRule`, `MapLengthToTokens`):

```csharp
    /// <summary>Builds a prompt for an interviewer follow-up on a captured screen task: the model
    /// either answers a question about the task or emits an updated solution incorporating all
    /// accumulated requirements. Captured OCR, additions, transcript, and prior answer are fenced
    /// and labeled as untrusted data.</summary>
    /// <param name="profile">Candidate's code profile.</param>
    /// <param name="settings">Answer settings.</param>
    /// <param name="screenContext">Combined OCR of the captured task.</param>
    /// <param name="mode">The screen analysis mode the capture used.</param>
    /// <param name="additions">Accumulated interviewer additions (oldest → newest).</param>
    /// <param name="recentTranscript">Recent transcript lines for interpreting terse replies.</param>
    /// <param name="priorAnswer">The most recent prior answer in the lineage, or <see langword="null"/>.</param>
    public static AnswerPrompt BuildScreenFollowUp(
        CodeProfile profile,
        AnswerSettings settings,
        string screenContext,
        ScreenAnalysisMode mode,
        IReadOnlyList<string> additions,
        IReadOnlyList<string> recentTranscript,
        string? priorAnswer)
    {
        var system = new StringBuilder();
        system.AppendLine(ModeSystemPrompt(mode));
        AppendCodeProfile(system, profile);
        system.AppendLine(SharedMarkdownRule);
        system.AppendLine("Use only as many tokens as the answer genuinely needs — be complete but " +
            "concise; do not pad, repeat, or add filler to fill space.");
        system.AppendLine("The interviewer has added requirements to, or asked about, the task on " +
            "screen. If they added conditions, give the UPDATED solution incorporating ALL listed " +
            "requirements (complete and runnable). If they asked a question about the task or your " +
            "approach, answer it directly and briefly. Do not restate the task. Decide which from " +
            "their words.");

        var user = new StringBuilder();
        user.AppendLine("On-screen task (OCR):");
        user.AppendLine(screenContext);
        user.AppendLine();
        user.AppendLine("Interviewer requirements (most recent last):");
        for (var i = 0; i < additions.Count; i++)
            user.AppendLine(CultureInfo.InvariantCulture, $"{i + 1}. {additions[i]}");

        if (recentTranscript is { Count: > 0 })
        {
            user.AppendLine();
            user.AppendLine("Recent conversation:");
            foreach (var line in recentTranscript)
                user.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        }

        if (!string.IsNullOrWhiteSpace(priorAnswer))
        {
            user.AppendLine();
            user.AppendLine("Your previous answer:");
            user.AppendLine(priorAnswer);
        }

        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: Math.Max(MapLengthToTokens(settings.Length), 2000));
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~PromptBuilderScreenFollowUpTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs tests/AIHelperNET.Application.Tests/Answers/PromptBuilderScreenFollowUpTests.cs
git commit -m "feat(app): BuildScreenFollowUp prompt for captured-task interviewer follow-ups"
```

---

### Task 6: `GenerateScreenFollowUpCommand` + handler

**Files:**
- Create: `src/AIHelperNET.Application/Answers/Commands/GenerateScreenFollowUpCommand.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/GenerateScreenFollowUpHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class GenerateScreenFollowUpHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static async IAsyncEnumerable<string> Stream(params string[] chunks)
    {
        foreach (var c in chunks) { yield return c; await Task.Yield(); }
    }

    // Builds a session whose only turn is the capture card "A" with a completed answer.
    private static (Session session, ConversationTurn cardA) SessionWithCapture()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var a = session.AddConversationTurn(q.Id, "Implement LRU cache", T0).Value;
        a.TransitionTo(ConversationTurnStatus.GeneratingRefined);
        a.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, "class LruCache {}", T0));
        a.TransitionTo(ConversationTurnStatus.RefinedReady);
        return (session, a);
    }

    private static (GenerateScreenFollowUpHandler handler, IConversationTurnSink turnSink,
                    IAnswerStreamSink streamSink, ScreenTaskContextStore store, IAnswerProvider provider)
        Make(Session session)
    {
        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok(session)));

        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stream("updated ", "solution"));
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Base, AnswerSettings.Default, CodeProfile.Empty,
            MicDeviceId: null, LoopbackDeviceId: null)));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));
        var store = new ScreenTaskContextStore();

        var handler = new GenerateScreenFollowUpHandler(
            repo, resolver, settings, streamSink, turnSink, uow, store, TimeProvider.System);
        return (handler, turnSink, streamSink, store, provider);
    }

    [Fact]
    public async Task Handle_CreatesNewCard_StreamsScreenFollowUp_DoesNotTouchCaptureCard()
    {
        var (session, cardA) = SessionWithCapture();
        var (handler, turnSink, streamSink, store, _) = Make(session);

        var result = await handler.Handle(new GenerateScreenFollowUpCommand(
            session.Id, cardA.Id, "Implement LRU cache", "Implement LRU cache",
            ScreenAnalysisMode.SolveCodingTask,
            new[] { "make it thread-safe" }, Array.Empty<string>()), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        session.ConversationTurns.Should().HaveCount(2, "a new follow-up card was added");
        cardA.AnswerVersions.Should().ContainSingle("the capture card is never mutated");

        var newCard = session.ConversationTurns.Single(t => t.Id != cardA.Id);
        newCard.AnswerVersions.Should().ContainSingle()
            .Which.Type.Should().Be(AnswerVersionType.ScreenFollowUp);
        turnSink.Received(1).OnTurnCreated(newCard.Id, "Implement LRU cache");
        store.Current.Should().BeNull("Register is the VM's job; the handler only sets latest card when a task exists");
    }

    [Fact]
    public async Task Handle_PassesPriorAnswerFromParent_IntoPrompt()
    {
        var (session, cardA) = SessionWithCapture();
        var (handler, _, _, _, provider) = Make(session);

        AnswerPrompt? captured = null;
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.ArgAt<AnswerPrompt>(0); return Stream("ok"); });

        await handler.Handle(new GenerateScreenFollowUpCommand(
            session.Id, cardA.Id, "Implement LRU cache", "Implement LRU cache",
            ScreenAnalysisMode.SolveCodingTask,
            new[] { "make it thread-safe" }, Array.Empty<string>()), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.User.Should().Contain("class LruCache {}");   // parent answer carried forward
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateScreenFollowUpHandlerTests"`
Expected: FAIL — command/handler not defined.

- [ ] **Step 3: Write minimal implementation**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Creates a NEW card answering an interviewer follow-up about a captured screen task,
/// using the captured OCR, all accumulated additions, and the parent card's prior answer.</summary>
/// <param name="SessionId">The active session.</param>
/// <param name="ParentTurnId">The most recent card in the lineage (source of the prior answer).</param>
/// <param name="TopicLabel">The captured-task topic label (used as the new card's title).</param>
/// <param name="Ocr">Combined OCR of the captured task.</param>
/// <param name="Mode">The screen analysis mode the capture used.</param>
/// <param name="Additions">Accumulated interviewer additions (oldest → newest).</param>
/// <param name="RecentTranscript">Recent transcript lines for context.</param>
public sealed record GenerateScreenFollowUpCommand(
    SessionId SessionId,
    ConversationTurnId ParentTurnId,
    string TopicLabel,
    string Ocr,
    ScreenAnalysisMode Mode,
    IReadOnlyList<string> Additions,
    IReadOnlyList<string> RecentTranscript) : IRequest<Result>;

/// <summary>Handles <see cref="GenerateScreenFollowUpCommand"/>.</summary>
public sealed class GenerateScreenFollowUpHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IConversationTurnSink turnSink,
    IUnitOfWork unitOfWork,
    ScreenTaskContextStore screenStore,
    TimeProvider clock) : IRequestHandler<GenerateScreenFollowUpCommand, Result>
{
    private const int PriorAnswerCap = 1200;

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(GenerateScreenFollowUpCommand request, CancellationToken cancellationToken)
    {
        var settings = await settingsStore.LoadAsync(cancellationToken);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(request.SessionId, cancellationToken);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var now = clock.GetUtcNow();
        var question = DetectedQuestion.Create(request.TopicLabel, QuestionSource.Ocr, now);
        session.AddDetectedQuestion(question);
        var turnResult = session.AddConversationTurn(question.Id, request.TopicLabel, now);
        if (turnResult.IsFailed) return Result.Fail(turnResult.Error);
        var turn = turnResult.Value;

        // Persist + notify before streaming so a failed create never leaves a phantom card.
        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return save;
        turnSink.OnTurnCreated(turn.Id, request.TopicLabel);

        // Best-effort prior answer from the parent card (only completed answers are stored).
        var parent = session.ConversationTurns.FirstOrDefault(t => t.Id == request.ParentTurnId);
        var priorText = parent?.AnswerVersions
            .OrderByDescending(v => v.CreatedAt).FirstOrDefault()?.Text;
        var priorAnswer = string.IsNullOrWhiteSpace(priorText)
            ? null
            : priorText!.Length > PriorAnswerCap ? priorText[..PriorAnswerCap] + "…" : priorText;

        var start = session.StartAnswer(turn.InitialQuestionId, now);
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;
        turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

        var prompt = PromptBuilderService.BuildScreenFollowUp(
            session.CodeProfile, session.AnswerSettings,
            request.Ocr, request.Mode, request.Additions, request.RecentTranscript, priorAnswer);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, cancellationToken))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(turn.Id, AnswerVersionType.ScreenFollowUp, chunk, cancellationToken);
            }
            answer.Complete(clock.GetUtcNow());
            turn.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.ScreenFollowUp, chunks.ToString(), clock.GetUtcNow()));
            turn.TransitionTo(ConversationTurnStatus.RefinedReady);
            await streamSink.OnCompleteAsync(turn.Id, AnswerVersionType.ScreenFollowUp, cancellationToken);
            screenStore.SetLatestCard(turn.Id);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(turn.Id, AnswerErrorMessage.ForUser(ex), cancellationToken);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateScreenFollowUpHandlerTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Commands/GenerateScreenFollowUpCommand.cs tests/AIHelperNET.Application.Tests/Answers/GenerateScreenFollowUpHandlerTests.cs
git commit -m "feat(app): GenerateScreenFollowUp command + handler (new context-aware card)"
```

---

### Task 7: Register `ScreenTaskContextStore` in DI

**Files:**
- Modify: `src/AIHelperNET.Application/DependencyInjection.cs`

- [ ] **Step 1: Add the registration**

After `services.AddSingleton<PromptBuilderService>();` add:

```csharp
        services.AddSingleton<Answers.ScreenTaskContextStore>();
```

- [ ] **Step 2: Build**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Application/DependencyInjection.cs
git commit -m "chore(di): register ScreenTaskContextStore singleton"
```

---

### Task 8: Pipeline — screen follow-up branch, classify, debounced fire, reset

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineScreenFollowUpTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TranscriptPipelineScreenFollowUpTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static BoundaryClassificationResult Label(BoundaryLabel l) =>
        new(l, 0.95, false, false, false, "x", "test");

    // Session pre-seeded with a capture card; store registered for that card.
    private static (TranscriptPipelineService svc, Session session, ScreenTaskContextStore store, IUnitOfWork uow)
        Make(BoundaryLabel classifierReturns)
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Implement LRU cache", QuestionSource.Ocr, T0);
        session.AddDetectedQuestion(q);
        var cardA = session.AddConversationTurn(q.Id, "Implement LRU cache", T0).Value;

        var store = new ScreenTaskContextStore();
        store.Register(cardA.Id, "Implement LRU cache", ScreenAnalysisMode.SolveCodingTask, isNewGroup: true);

        var boundary = Substitute.For<IQuestionBoundaryClassifier>();
        boundary.ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Label(classifierReturns)));

        var legacy = Substitute.For<IQuestionClassifier>();
        var mediator = Substitute.For<IMediator>();
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);
        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var transcriptSink = Substitute.For<ITranscriptSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var svc = new TranscriptPipelineService(
            factory, transcriptSink, turnSink, legacy,
            boundaryClassifier: boundary, screenStore: store);
        return (svc, session, store, uow);
    }

    [Fact]
    public async Task InterviewerAddition_WhileScreenTaskActive_AccumulatesAddition()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.AdditionalRequirement);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "now make it thread-safe", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().ContainSingle().Which.Should().Be("now make it thread-safe");
    }

    [Fact]
    public async Task InterviewerNewQuestion_WhileScreenTaskActive_ClearsLinkage()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.NewQuestion);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "next question, explain hash maps", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().BeNull("an interviewer new question drops the screen-task linkage");
    }

    [Fact]
    public async Task InterviewerNoise_WhileScreenTaskActive_LeavesLinkageUntouched()
    {
        var (svc, session, store, uow) = Make(BoundaryLabel.NoQuestion);

        await svc.ProcessAsync(session,
            TranscriptItem.Create(Speaker.Other, "hmm, okay", T0.AddSeconds(2), 0.9f),
            uow, CancellationToken.None);

        store.Current.Should().NotBeNull();
        store.Current!.Additions.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineScreenFollowUpTests"`
Expected: FAIL — the `screenStore:` constructor parameter and the branch don't exist.

- [ ] **Step 3: Add the constructor parameter + fields**

In `TranscriptPipelineService.cs`, add a trailing optional constructor parameter (after `recorder`):

```csharp
    IBoundaryDecisionRecorder? recorder = null,
    Answers.ScreenTaskContextStore? screenStore = null) : IDisposable
```

Add these fields next to the existing private fields (near line 35):

```csharp
    private readonly Answers.ScreenTaskContextStore _screenStore = screenStore ?? new Answers.ScreenTaskContextStore();
    private SessionId? _sessionId;
    private IReadOnlyList<string> _recentTranscriptSnapshot = [];
```

- [ ] **Step 4: Stamp the session id at the top of `ProcessAsync`**

In `ProcessAsync`, immediately after `session.AddTranscriptItem(item);` add:

```csharp
        _sessionId = session.Id;
```

- [ ] **Step 5: Add the screen follow-up branch**

In `BuildCommandWithBoundaryAsync`, immediately after the existing Me short-circuit
(`if (item.Speaker == Speaker.Me) return HandleMeUtterance(session, item);`), insert:

```csharp
        // Screen-task follow-up: while a captured task is in focus, interviewer speech that adds to
        // or asks about it spawns a NEW context-aware card (the capture card is never mutated).
        if (_screenStore.Current is { } screenCtx)
        {
            switch (await ClassifyScreenFollowUpAsync(item, ct))
            {
                case Answers.ScreenFollowUpOutcome.FollowUp:
                    _screenStore.AddAddition(item.Text);
                    _recentTranscriptSnapshot = SnapshotRecentTranscript();
                    _regenDebouncer.Touch(screenCtx.ScreenCardId, FireScreenFollowUp);
                    return null;
                case Answers.ScreenFollowUpOutcome.MovedOn:
                    _screenStore.Clear();   // fall through to normal audio routing below
                    break;
                default:
                    return null;            // Noise — ignore
            }
        }
```

- [ ] **Step 6: Add the helper methods**

Add these private methods to the class (e.g. just below `HandleMeUtterance`):

```csharp
    /// <summary>Classifies an interviewer utterance against the captured screen task. Prefers the AI
    /// boundary classifier (the heuristic was tuned for audio turns); falls back to the heuristic.</summary>
    private async Task<Answers.ScreenFollowUpOutcome> ClassifyScreenFollowUpAsync(
        TranscriptItem item, CancellationToken ct)
    {
        if (boundaryClassifier is not null)
        {
            try
            {
                var r = await boundaryClassifier.ClassifyAsync(
                    ConversationTurnStatus.RefinedReady, _recentItems.AsReadOnly(), item, item.Speaker, ct);
                return Answers.ScreenFollowUpRouter.Map(r.Classification);
            }
            catch (Exception ex)
            {
                if (logger is not null)
                    Log.BoundaryClassifierFailed(logger, ex, item.Text[..Math.Min(80, item.Text.Length)]);
            }
        }

        var h = _boundaryDetector.Evaluate(item.Text, item.Speaker, ConversationTurnStatus.RefinedReady, []);
        return Answers.ScreenFollowUpRouter.Map(h.Classification);
    }

    /// <summary>Snapshots the last few transcript lines (both speakers) for follow-up prompt context.</summary>
    private IReadOnlyList<string> SnapshotRecentTranscript()
        => _recentItems.TakeLast(4)
            .Select(i => $"{(i.Speaker == Speaker.Me ? "Me" : "Interviewer")}: {i.Text}")
            .ToArray();

    /// <summary>Fires a screen follow-up command (debounced) in a fresh scope. Runs on a timer thread,
    /// so it reads the thread-safe store snapshot and must not touch the pipeline's session.</summary>
    private void FireScreenFollowUp()
    {
        var ctx = _screenStore.Current;
        if (ctx is null || _sessionId is not { } sid) return;

        var cmd = new GenerateScreenFollowUpCommand(
            sid, ctx.LatestCardId, ctx.TopicLabel, ctx.Ocr, ctx.Mode, ctx.Additions, _recentTranscriptSnapshot);

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(cmd, CancellationToken.None);
        }, CancellationToken.None);
    }
```

- [ ] **Step 7: Clear screen state in `Reset`**

In `Reset()`, add after `_regenDebouncer.Reset();`:

```csharp
        _screenStore.Clear();
        _sessionId = null;
        _recentTranscriptSnapshot = [];
```

- [ ] **Step 8: Run the new tests**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineScreenFollowUpTests"`
Expected: PASS (3 tests).

- [ ] **Step 9: Run the existing pipeline tests (regression guard)**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineService"`
Expected: PASS — existing audio routing unchanged (the screen branch is unreachable without a registered task).

- [ ] **Step 10: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineScreenFollowUpTests.cs
git commit -m "feat(pipeline): detect interviewer follow-ups on a captured screen task"
```

---

### Task 9: VM — register the captured task with the bridge

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`

- [ ] **Step 1: Check for VM constructor call sites**

Run: `rg -n "new ConversationTurnViewModel\(" --glob "*.cs"`
Expected: note any results (e.g. tests). Each must be updated in Step 4 to pass a `ScreenTaskContextStore`.

- [ ] **Step 2: Inject the store into the VM constructor**

Change the primary constructor (line ~188) from:

```csharp
public sealed partial class ConversationTurnViewModel(IMediator mediator, TimeProvider clock) : ObservableObject
```

to:

```csharp
public sealed partial class ConversationTurnViewModel(
    IMediator mediator, TimeProvider clock, AIHelperNET.Application.Answers.ScreenTaskContextStore screenStore) : ObservableObject
```

- [ ] **Step 3: Register the captured task after a successful capture generation**

In `CaptureScreenAsync`, inside the `try`, immediately after the existing
`_screenAccumulator.Touch(clock.GetUtcNow());` (line ~406, after the awaited
`RegenerateAnswerWithScreenCommand`), add:

```csharp
            // Bridge the captured task to the audio pipeline so a later interviewer follow-up can
            // answer with this OCR as context. Runs only on the winning (non-cancelled) capture.
            screenStore.Register(turnId, add.CombinedOcr, sessionControl.ScreenAnalysisMode, add.IsNewGroup);
```

- [ ] **Step 4: Update any VM construction sites found in Step 1**

For each call site, pass a store instance. In production this is DI-resolved (Task 7). In tests,
construct with `new AIHelperNET.Application.Answers.ScreenTaskContextStore()`.

- [ ] **Step 5: Build the app**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat(app): register captured task with the screen-task bridge after capture"
```

---

### Task 10: Full build, test sweep, and manual verification

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 2: Full unit/integration test run**

Run: `dotnet test tests/AIHelperNET.Application.Tests tests/AIHelperNET.Domain.Tests tests/AIHelperNET.Integration.Tests`
Expected: all PASS. (No migration was added, so `MigrationTests` parity is unaffected.)

- [ ] **Step 3: Manual verification (real app)**

Using the `run-aihelper` skill, launch the app and:
1. Start a session; capture a coding task (Ctrl+Shift+S) → confirm Card A answers.
2. Speak as the interviewer: "now make it thread-safe" → confirm a **new** Card B appears
   (titled with the task topic) with an updated solution, and **Card A is unchanged**.
3. Speak: "and handle nulls" → confirm Card C incorporates **both** conditions.
4. Speak a clearly new question: "next question, what is a hash map?" → confirm a normal audio
   card with no screen context (linkage ended).

Expected: matches the data-flow walkthrough in the spec.

- [ ] **Step 4: Final commit (if any verification fixes were needed)**

```bash
git add -A
git commit -m "test: verify screen-task interviewer follow-up end to end"
```

---

## Self-review (performed during planning)

- **Spec coverage:** automatic trigger (Task 8), always-new-card / never-mutate (Task 6 + handler test), accumulate-all (Task 3 `AddAddition` + Task 5 prompt), answer-or-update decision (Task 5 system prompt), lifetime/Moved-on (Task 8 branch + test), in-memory bridge no-migration (Tasks 3/7), Improvement A debounce (Task 8 `_regenDebouncer.Touch`), Improvement B topic label (Task 2 + used in Tasks 3/6), Improvement C recent transcript (Task 8 `SnapshotRecentTranscript` → Task 5 prompt). Compatibility: multi-capture untouched (VM `Register` is additive — Task 9), audio routing guarded by `_screenStore.Current` (Task 8 regression run, Step 9).
- **Type consistency:** `ScreenTaskContext` fields, `ScreenTaskContextStore` methods (`Register`/`AddAddition`/`SetLatestCard`/`Clear`/`Current`), `ScreenFollowUpOutcome` (`FollowUp`/`MovedOn`/`Noise`), `GenerateScreenFollowUpCommand` parameter order, and `BuildScreenFollowUp` signature are used identically across Tasks 3–9.
- **Pre-verified against source:** `ConversationTurnId.New()` exists; `BoundaryLabel` member names match the router exactly. No open confirm-before-coding items remain.
