# Conversation Core Implementation Plan (Spec 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the in-memory `Session` the single authoritative live conversation state by closing the status loop from the background answer worker back to the pipeline, stop the persistence clobbering, give every turn its own answer-cancellation token, and route `Me` utterances deterministically (no AI, never opening a turn).

**Architecture:** Approach 1 — the orchestrator (`TranscriptPipelineService`) owns live state. A new singleton in-memory channel (`ITurnStatusFeedback`) carries `TurnStatusEvent`s from `GenerateAnswerHandler` (separate DI scope) back to the pipeline, which drains and applies them to its in-memory turns at the top of every `ProcessAsync`. The DB becomes write-through (no full-graph `repository.Update`). Answer cancellation moves from one shared CTS to a per-turn `ConcurrentDictionary`.

**Tech Stack:** .NET 10, C# latest, `System.Threading.Channels`, Mediator (source-gen CQRS), EF Core/SQLite, xUnit + FluentAssertions + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-06-08-conversation-core-design.md`

---

## File Structure

**New files**
- `src/AIHelperNET.Application/Abstractions/ITurnStatusFeedback.cs` — port interface + `TurnStatusEvent` record struct.
- `src/AIHelperNET.Application/Sessions/TurnStatusFeedback.cs` — singleton `Channel<TurnStatusEvent>` implementation.
- `tests/AIHelperNET.Application.Tests/Sessions/TurnStatusFeedbackTests.cs` — unit tests for the channel.
- `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs` — handler unit tests (new file).

**Modified files**
- `src/AIHelperNET.Domain/Sessions/Session.cs` — add `LastTurn` query.
- `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` — per-turn CTS registry; drain feedback; deterministic `Me` routing.
- `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs` — handler injects `ITurnStatusFeedback`, publishes transitions, drops `repository.Update`, folds clarification items into the prompt context.
- `src/AIHelperNET.Application/DependencyInjection.cs` — register `ITurnStatusFeedback` singleton.
- `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` — update both `MakeSvc*` helpers to supply feedback; add new behaviour tests.
- `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs` — test `LastTurn`.
- `tests/AIHelperNET.Integration.Tests/Persistence/SessionPersistenceTests.cs` — persistence-partition test.

**Responsibilities / boundaries**
- `ITurnStatusFeedback` is the *only* cross-scope channel for turn status. Producer = answer handler; consumer = pipeline.
- The pipeline owns pre-answer states (`Detected`, `CollectingQuestion`, `AwaitingClarification`, `ClarificationReceived`) + structure + clarification IDs. The answer handler owns `Generating*`/`*Ready` + answer versions. They never write the same column, so the feedback sync is last-write-wins-harmless.

---

## Task 1: `ITurnStatusFeedback` port + channel implementation + DI

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/ITurnStatusFeedback.cs`
- Create: `src/AIHelperNET.Application/Sessions/TurnStatusFeedback.cs`
- Modify: `src/AIHelperNET.Application/DependencyInjection.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TurnStatusFeedbackTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/Sessions/TurnStatusFeedbackTests.cs`:

```csharp
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TurnStatusFeedbackTests
{
    [Fact]
    public void TryDrain_WhenEmpty_ReturnsFalse()
    {
        var sut = new TurnStatusFeedback();
        sut.TryDrain(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_ThenDrain_ReturnsSameEvent()
    {
        var sut = new TurnStatusFeedback();
        var turnId = ConversationTurnId.New();
        sut.Publish(new TurnStatusEvent(turnId, ConversationTurnStatus.PreliminaryReady));

        sut.TryDrain(out var e).Should().BeTrue();
        e.TurnId.Should().Be(turnId);
        e.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        sut.TryDrain(out _).Should().BeFalse();
    }

    [Fact]
    public void Publish_PreservesFifoOrder()
    {
        var sut = new TurnStatusFeedback();
        var t1 = ConversationTurnId.New();
        var t2 = ConversationTurnId.New();
        sut.Publish(new TurnStatusEvent(t1, ConversationTurnStatus.GeneratingPreliminary));
        sut.Publish(new TurnStatusEvent(t2, ConversationTurnStatus.GeneratingPreliminary));
        sut.Publish(new TurnStatusEvent(t1, ConversationTurnStatus.PreliminaryReady));

        sut.TryDrain(out var a).Should().BeTrue();
        sut.TryDrain(out var b).Should().BeTrue();
        sut.TryDrain(out var c).Should().BeTrue();
        a.TurnId.Should().Be(t1);
        a.Status.Should().Be(ConversationTurnStatus.GeneratingPreliminary);
        b.TurnId.Should().Be(t2);
        c.TurnId.Should().Be(t1);
        c.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TurnStatusFeedbackTests"`
Expected: FAIL — compile error `TurnStatusFeedback`/`TurnStatusEvent` do not exist.

- [ ] **Step 3: Create the port interface**

Create `src/AIHelperNET.Application/Abstractions/ITurnStatusFeedback.cs`:

```csharp
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// A turn-lifecycle status transition reported by the background answer worker.
/// </summary>
/// <param name="TurnId">The conversation turn whose status changed.</param>
/// <param name="Status">The new status the turn transitioned to.</param>
public readonly record struct TurnStatusEvent(ConversationTurnId TurnId, ConversationTurnStatus Status);

/// <summary>
/// One-way, in-memory feedback channel from <c>GenerateAnswerHandler</c> (which runs in a separate
/// DI scope) back to <c>TranscriptPipelineService</c>, so the pipeline's authoritative in-memory
/// <c>Session</c> can be kept in sync with answer-generation progress.
/// </summary>
public interface ITurnStatusFeedback
{
    /// <summary>Publishes a status transition. Safe to call from any thread.</summary>
    /// <param name="statusEvent">The transition to report.</param>
    void Publish(TurnStatusEvent statusEvent);

    /// <summary>
    /// Removes and returns the next pending event if one is available.
    /// </summary>
    /// <param name="statusEvent">The dequeued event when this returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> if an event was drained; otherwise <see langword="false"/>.</returns>
    bool TryDrain(out TurnStatusEvent statusEvent);
}
```

- [ ] **Step 4: Create the channel implementation**

Create `src/AIHelperNET.Application/Sessions/TurnStatusFeedback.cs`:

```csharp
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Unbounded, thread-safe <see cref="ITurnStatusFeedback"/> backed by a
/// <see cref="Channel{T}"/>. Registered as a singleton so the answer handler (multi-writer)
/// and the pipeline (single reader) share one instance.
/// </summary>
public sealed class TurnStatusFeedback : ITurnStatusFeedback
{
    private readonly Channel<TurnStatusEvent> _channel =
        Channel.CreateUnbounded<TurnStatusEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    /// <inheritdoc/>
    public void Publish(TurnStatusEvent statusEvent) => _channel.Writer.TryWrite(statusEvent);

    /// <inheritdoc/>
    public bool TryDrain(out TurnStatusEvent statusEvent) => _channel.Reader.TryRead(out statusEvent);
}
```

- [ ] **Step 5: Register the singleton in DI**

In `src/AIHelperNET.Application/DependencyInjection.cs`, add the registration next to the other application singletons (after `services.AddSingleton<TranscriptPipelineService>();`):

```csharp
        services.AddSingleton<ITurnStatusFeedback, TurnStatusFeedback>();
```

Add the required usings at the top of the file if not present:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TurnStatusFeedbackTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/ITurnStatusFeedback.cs \
        src/AIHelperNET.Application/Sessions/TurnStatusFeedback.cs \
        src/AIHelperNET.Application/DependencyInjection.cs \
        tests/AIHelperNET.Application.Tests/Sessions/TurnStatusFeedbackTests.cs
git commit -m "feat: add ITurnStatusFeedback channel for turn-status sync"
```

---

## Task 2: `Session.LastTurn` query

`Me` routing needs "the most recent turn overall" for the all-terminal follow-up edge (when there is no non-terminal `ActiveTurn` but turns exist).

**Files:**
- Modify: `src/AIHelperNET.Domain/Sessions/Session.cs:51-55` (add property next to `ActiveTurn`)
- Test: `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs`:

```csharp
    [Fact]
    public void LastTurn_ReturnsMostRecentTurn_EvenWhenTerminal()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        var q1 = DetectedQuestion.Create("Q1", QuestionSource.Audio, DateTimeOffset.UnixEpoch);
        var q2 = DetectedQuestion.Create("Q2", QuestionSource.Audio, DateTimeOffset.UnixEpoch);
        session.AddDetectedQuestion(q1);
        session.AddDetectedQuestion(q2);
        var t1 = session.AddConversationTurn(q1.Id, "Q1", DateTimeOffset.UnixEpoch).Value;
        var t2 = session.AddConversationTurn(q2.Id, "Q2", DateTimeOffset.UnixEpoch).Value;

        t1.Resolve();
        t2.Resolve(); // both terminal → ActiveTurn is null

        session.ActiveTurn.Should().BeNull();
        session.LastTurn.Should().Be(t2);
    }

    [Fact]
    public void LastTurn_ReturnsNull_WhenNoTurns()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        session.LastTurn.Should().BeNull();
    }
```

> If `SessionTests.cs` lacks the `DetectedQuestion`/`QuestionSource`/`FluentAssertions` usings, add:
> `using AIHelperNET.Domain.Sessions;`, `using AIHelperNET.Domain.ValueObjects;`, `using FluentAssertions;`, `using Xunit;` (match the existing file header — most are likely already present).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~LastTurn"`
Expected: FAIL — `Session` has no `LastTurn`.

- [ ] **Step 3: Add the property**

In `src/AIHelperNET.Domain/Sessions/Session.cs`, immediately after the `ActiveTurn` property (line 55), add:

```csharp
    /// <summary>The most recent conversation turn overall (including terminal turns), or <see langword="null"/> if none.</summary>
    public ConversationTurn? LastTurn => _turns.Count == 0 ? null : _turns[^1];
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~LastTurn"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Domain/Sessions/Session.cs tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs
git commit -m "feat: add Session.LastTurn for follow-up routing"
```

---

## Task 3: Per-turn answer cancellation in the pipeline

Replace the single `_currentAnswerCts` with a `ConcurrentDictionary<ConversationTurnId, CancellationTokenSource>`. Cancellation of a turn happens **only** when that same turn fires a new generation; distinct turns never cancel each other.

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `TranscriptPipelineServiceTests.cs` (boundary-path region). This test uses the real heuristic (the `NewQuestion` shape is recognised at high confidence). Two distinct interviewer questions must each fire an answer and the first must NOT be cancelled:

```csharp
    [Fact]
    public async Task BoundaryPath_TwoDistinctQuestions_DoNotCancelEachOther()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Capture the cancellation token each GenerateAnswerCommand was dispatched with.
        var tokens = new List<CancellationToken>();
        var neverCalled = Substitute.For<IQuestionBoundaryClassifier>();
        neverCalled.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BoundaryClassificationResult.Ambiguous("fallback")));

        var (svc, mediator, _, uow) = MakeSvcWithBoundary(transcriptSink, neverCalled);
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(ci => { tokens.Add(ci.ArgAt<CancellationToken>(1)); return new ValueTask<Result>(Result.Ok()); });
#pragma warning restore CA2012

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "What exactly is dependency injection?"), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "What exactly is the repository pattern?"), uow, CancellationToken.None);
        await Task.Delay(200);

        session.ConversationTurns.Should().HaveCount(2);
        tokens.Should().HaveCount(2);
        tokens[0].IsCancellationRequested.Should().BeFalse("a second distinct question must not cancel the first");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TwoDistinctQuestions_DoNotCancelEachOther"`
Expected: FAIL — `tokens[0].IsCancellationRequested` is `true` (the shared `_currentAnswerCts` cancelled the first when the second fired).

- [ ] **Step 3: Add the per-turn CTS registry and a helper, remove the field**

In `TranscriptPipelineService.cs`:

Add the using at the top:

```csharp
using System.Collections.Concurrent;
```

Replace the field declaration (line 23):

```csharp
    private CancellationTokenSource? _currentAnswerCts;
```

with:

```csharp
    private readonly ConcurrentDictionary<ConversationTurnId, CancellationTokenSource> _turnCts = new();
```

- [ ] **Step 4: Route all generation through a per-turn token in `FireAndForget`**

Replace `FireAndForget` (lines 371-382) with:

```csharp
    private void FireAndForget(GenerateAnswerCommand command, CancellationToken sessionCt)
    {
        // Each turn owns its own CTS. Re-firing a turn cancels that turn's prior in-flight
        // generation (same-turn regeneration); distinct turns are independent and never cancel
        // each other.
        var cts = _turnCts.AddOrUpdate(
            command.TurnId,
            _ => new CancellationTokenSource(),
            (_, old) => { old.Cancel(); old.Dispose(); return new CancellationTokenSource(); });
        var requestCt = cts.Token;

        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediator    = scope.ServiceProvider.GetRequiredService<IMediator>();
            // Use the per-turn token so a regeneration of THIS turn cancels this generation.
            // Session stop is ignored here intentionally (fire-and-forget completes on its own).
            await mediator.Send(command, requestCt);
        }, CancellationToken.None);
    }
```

- [ ] **Step 5: Delete the now-dead manual CTS swaps in the handlers**

The old `var old = _currentAnswerCts; _currentAnswerCts = new(); old?.Cancel(); old?.Dispose();` blocks are obsolete — cancellation now happens in `FireAndForget`. Remove those four lines from each of:

- `HandleQuestionComplete` (lines 207-210),
- `HandleAdditionalRequirement` (lines 228-231),
- `HandleClarification` Other branch (lines 262-265),
- `ForceCompleteCollection` (lines 273-276).

For example, `HandleQuestionComplete` becomes:

```csharp
    private GenerateAnswerCommand? HandleQuestionComplete(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        _collectionStartedAt = null;

        if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
        {
            // Complete the collected fragments into a full question
            activeTurn.AddFragment(item.Text);
            activeTurn.CompleteQuestion();
            turnSink.OnTurnStatusChanged(activeTurn.Id, ConversationTurnStatus.Detected);
            return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
        }

        // No collecting turn — create new
        return HandleNewQuestion(session, item.Text, item.Timestamp);
    }
```

`HandleAdditionalRequirement` becomes:

```csharp
    private GenerateAnswerCommand? HandleAdditionalRequirement(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null) return null;
        activeTurn.AppendToQuestion(item.Text);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }
```

`HandleClarification` Other branch becomes (keep the new-question guard; drop only the CTS swap):

```csharp
            // Other speaker adds clarification context
            activeTurn.AttachClarificationResponse(item.Id);
            if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);
            return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification);
```

`ForceCompleteCollection` becomes:

```csharp
    private GenerateAnswerCommand? ForceCompleteCollection(Session session, ConversationTurn activeTurn)
    {
        _collectionStartedAt = null;
        activeTurn.CompleteQuestion();
        turnSink.OnTurnStatusChanged(activeTurn.Id, ConversationTurnStatus.Detected);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }
```

- [ ] **Step 6: Update `Dispose` to drain the registry**

Replace `Dispose` (lines 365-369) with:

```csharp
    /// <summary>Releases resources owned by this service.</summary>
    public void Dispose()
    {
        foreach (var cts in _turnCts.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _turnCts.Clear();
    }
```

- [ ] **Step 7: Run the full pipeline test suite to verify it passes and nothing regressed**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS — including the new `BoundaryPath_TwoDistinctQuestions_DoNotCancelEachOther` and all pre-existing pipeline tests.

- [ ] **Step 8: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs \
        tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat: per-turn answer cancellation so distinct questions run in parallel"
```

---

## Task 4: Drain feedback into the in-memory session

Inject `ITurnStatusFeedback` into the pipeline and, at the top of `ProcessAsync`, apply every pending `TurnStatusEvent` to the matching in-memory turn. This is what un-sticks `ActiveTurn` and makes Rule 8 (`AdditionalRequirement`) fire in-process.

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Modify: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` (update both `MakeSvc*` helpers; add a test)

- [ ] **Step 1: Update both test helpers to construct and return a feedback instance**

In `TranscriptPipelineServiceTests.cs`, change the return tuple of both helpers to also expose the feedback so tests can publish events.

Change the signature + return of `MakeSvc` (line 28-60):

```csharp
    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback)
        MakeSvc(ITranscriptSink sink, IQuestionClassifier? classifier = null)
    {
        // ... unchanged body up to the turnSink/uow creation ...
        var feedback = new TurnStatusFeedback();
        return (new TranscriptPipelineService(factory, sink, turnSink, classifier, feedback: feedback),
            mediator, turnSink, uow, feedback);
    }
```

Change the signature + return of `MakeSvcWithBoundary` (line 270-296):

```csharp
    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback)
        MakeSvcWithBoundary(ITranscriptSink sink, IQuestionBoundaryClassifier boundaryClassifier)
    {
        // ... unchanged body up to the uow creation ...
        var feedback = new TurnStatusFeedback();
        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier, feedback),
            mediator, turnSink, uow, feedback);
    }
```

Then update every existing call site that destructures these helpers to add the trailing element. Existing call sites use patterns like `var (svc, mediator, _, uow) = ...` and `var (svc, _, _, uow) = ...` and `var (svc, mediator, turnSink, uow) = ...`. Append `, _` (or `, feedback` where the test needs it) to each. Mechanical fix: run the build (Step 3) and the compiler lists every call site to update.

> The `feedback:` named argument is required in `MakeSvc` because `TranscriptPipelineService`'s constructor has `logger` and `boundaryClassifier` optional parameters before `feedback` (see Step 2).

- [ ] **Step 2: Write the failing test (feedback unlocks Rule 8)**

Add to `TranscriptPipelineServiceTests.cs`:

```csharp
    [Fact]
    public async Task BoundaryPath_FeedbackPreliminaryReady_UnlocksAdditionalRequirementRefine()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Seed a turn that the pipeline created and that the (separate-scope) answer handler has
        // since advanced to PreliminaryReady — delivered via the feedback channel, NOT a direct mutation.
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        // turn is still Detected in the pipeline's in-memory copy.

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.AdditionalRequirement, 0.95,
                ShouldGenerateAnswer: true, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "Also assume it's a web app.", Reason: "test")));

        var (svc, mediator, _, uow, feedback) = MakeSvcWithBoundary(transcriptSink, classifier);

        // The answer worker reports the turn reached PreliminaryReady.
        feedback.Publish(new TurnStatusEvent(turn.Id, ConversationTurnStatus.PreliminaryReady));

        var item = MakeItem(Speaker.Other, "Also assume it's a web app.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        // Drain was applied → in-memory status is now PreliminaryReady, so Rule 8 regenerates.
        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id
                && c.VersionType == AnswerVersionType.Preliminary),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~FeedbackPreliminaryReady_UnlocksAdditionalRequirementRefine"`
Expected: FAIL — constructor has no `feedback` parameter (compile error), and once added, the status stays `Detected` so the assertion fails.

- [ ] **Step 4: Add the constructor parameter**

In `TranscriptPipelineService.cs`, add `ITurnStatusFeedback? feedback = null` as the last primary-constructor parameter (lines 12-18):

```csharp
public sealed partial class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink,
    IQuestionClassifier classifier,
    ILogger<TranscriptPipelineService>? logger = null,
    IQuestionBoundaryClassifier? boundaryClassifier = null,
    ITurnStatusFeedback? feedback = null) : IDisposable
```

> It is nullable/optional purely for test-construction convenience; in production DI always supplies it (Task 1 registration). When null, the drain step is a no-op.

- [ ] **Step 5: Add the drain step at the top of `ProcessAsync`**

In `ProcessAsync`, immediately after `transcriptSink.OnTranscriptItem(item);` (line 33), add:

```csharp
        DrainStatusFeedback(session);
```

Then add the private method (place it near `ProcessAsync`, e.g. after `FlushAccumulatorAsync`):

```csharp
    /// <summary>
    /// Applies any pending turn-status transitions reported by the background answer worker to the
    /// pipeline's authoritative in-memory <paramref name="session"/>. Events for unknown or
    /// terminal turns are ignored. Disposes a turn's cancellation source once it reaches a
    /// ready/terminal status (its generation has finished).
    /// </summary>
    private void DrainStatusFeedback(Session session)
    {
        if (feedback is null) return;
        while (feedback.TryDrain(out var e))
        {
            var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == e.TurnId);
            if (turn is null) continue;
            turn.TransitionTo(e.Status); // no-op-safe: TransitionTo fails closed on terminal turns

            if (e.Status is ConversationTurnStatus.PreliminaryReady
                          or ConversationTurnStatus.RefinedReady
                          or ConversationTurnStatus.Dismissed
                          or ConversationTurnStatus.Resolved
                && _turnCts.TryRemove(e.TurnId, out var cts))
            {
                cts.Dispose();
            }
        }
    }
```

> `TransitionTo` returns a `DomainResult` and refuses to move a terminal turn — we intentionally ignore the result (best-effort sync). A turn already at the target status is harmlessly re-set.

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~FeedbackPreliminaryReady_UnlocksAdditionalRequirementRefine"`
Expected: PASS.

- [ ] **Step 7: Run the full pipeline suite (catches the helper call-site fixups)**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS. If compile errors list call sites that still destructure four elements, add the trailing `, _` to each and re-run.

- [ ] **Step 8: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs \
        tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat: drain turn-status feedback into in-memory session (unsticks ActiveTurn, Rule 8)"
```

---

## Task 5: Deterministic `Me` routing

`Me` utterances must bypass classification entirely and route deterministically per spec §3: never open a turn, never call AI, never generate. They attach context to a target turn and (only on an unanswered, not-generating turn) flip it to `AwaitingClarification`.

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Add three tests to `TranscriptPipelineServiceTests.cs`. They drive the boundary path (`MakeSvcWithBoundary`) and assert that the `Me` path never calls the classifier and never sends a command.

```csharp
    [Fact]
    public async Task BoundaryPath_Me_UnansweredTurn_AttachesClarificationAndAwaits_NoAi_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value; // status Detected (unanswered)

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Do they mean constructor injection specifically?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "Me must never open a turn");
        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().Contain(me.Id);
        await classifier.DidNotReceive().ClassifyAsync(
            Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
            Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>());
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_Me_AnsweredTurn_RecordsContextOnly_NoStatusChange_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // already answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Actually also cover keyed services.");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady, "answered-turn follow-up does not change status");
        turn.ClarificationQuestionIds.Should().Contain(me.Id);
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_Me_NoTurns_Holds_NoTurnCreated_NoGenerate()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        var me = MakeItem(Speaker.Me, "Wait, what do they mean?");
        await svc.ProcessAsync(session, me, uow, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
        await Task.Delay(100);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BoundaryPath_Me_"`
Expected: FAIL — today `Me` items flow through `BuildCommandWithBoundaryAsync` → the classifier (so `DidNotReceive` fails) and/or open turns.

- [ ] **Step 3: Add the `Me` speaker guard in the boundary path**

In `BuildCommandWithBoundaryAsync`, add the guard as the very first statement (before the force-complete check, line 94):

```csharp
        // Me utterances are routed deterministically: no AI, never open a turn, never generate.
        if (item.Speaker == Speaker.Me)
            return HandleMeUtterance(session, item);
```

- [ ] **Step 4: Add the `HandleMeUtterance` method**

Add this method to `TranscriptPipelineService.cs` (e.g. after `HandleClarification`):

```csharp
    /// <summary>
    /// Deterministically routes a candidate (<see cref="Speaker.Me"/>) utterance. Per the
    /// conversation model, <see cref="Speaker.Me"/> never opens a turn and never triggers generation;
    /// it only attaches context to the current turn. Target = the most recent non-terminal turn if
    /// one exists, else the most recent turn overall. With no turns, it holds.
    /// </summary>
    private GenerateAnswerCommand? HandleMeUtterance(Session session, TranscriptItem item)
    {
        item.SetBoundaryRole(BoundaryRole.Clarification);

        var target = session.ActiveTurn ?? session.LastTurn;
        if (target is null)
            return null; // no turns yet — hold

        // Record the candidate's utterance as clarification/context on the target turn.
        target.AttachClarificationQuestion(item.Id);

        // Only an unanswered, not-generating turn flips to AwaitingClarification (the next Other
        // response will regenerate incorporating this). An answered or in-flight turn records the
        // context only — no status change, no auto-regenerate (avoids racing the live answer).
        if (target.Status is ConversationTurnStatus.Detected
                          or ConversationTurnStatus.CollectingQuestion)
        {
            target.TransitionTo(ConversationTurnStatus.AwaitingClarification);
        }

        return null; // Me never generates
    }
```

> `SetBoundaryRole(BoundaryRole.Clarification)` mirrors what `LabelToRole`/the Other path already records for clarifications, keeping the transcript item's role consistent for the UI/exports.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BoundaryPath_Me_"`
Expected: PASS (3 tests).

- [ ] **Step 6: Run the full pipeline suite (ensure the legacy 3-state Me test still passes)**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS. The legacy `Me`/`PreliminaryReady` clarification path (lines 56-65, used only when `boundaryClassifier` is null) is untouched and its tests still pass.

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs \
        tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat: deterministic Me routing (no AI, never opens a turn, never generates)"
```

---

## Task 6: Answer handler publishes status + stops clobbering

`GenerateAnswerHandler` must publish each turn transition to the feedback channel and stop calling `repository.Update(session)` (which marks the whole graph `Modified` and overwrites pipeline-owned columns). EF change-tracking already persists the turn `Status` and new `AnswerVersion`.

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs` (new file)

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs`:

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
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class GenerateAnswerHandlerTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    private static async IAsyncEnumerable<string> Stream(params string[] chunks)
    {
        foreach (var c in chunks) { yield return c; await Task.Yield(); }
    }

    private static (GenerateAnswerHandler handler, TurnStatusFeedback feedback,
                    ISessionRepository repo, Session session, ConversationTurn turn)
        Make()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "Explain DI.", T0, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(_ => Stream("Dependency ", "injection."));
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(AppSettingsDto.Default));

        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var feedback = new TurnStatusFeedback();

        var handler = new GenerateAnswerHandler(
            repo, resolver, settings, streamSink, uow, TimeProvider.System, feedback);

        return (handler, feedback, repo, session, turn);
    }

    [Fact]
    public async Task Handle_PublishesGeneratingThenReady_AndDoesNotCallRepositoryUpdate()
    {
        var (handler, feedback, repo, session, turn) = Make();

        var result = await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        var published = new List<TurnStatusEvent>();
        while (feedback.TryDrain(out var e)) published.Add(e);

        published.Select(e => e.Status).Should().ContainInOrder(
            ConversationTurnStatus.GeneratingPreliminary,
            ConversationTurnStatus.PreliminaryReady);
        published.Should().OnlyContain(e => e.TurnId == turn.Id);

        repo.DidNotReceive().Update(Arg.Any<Session>());
    }
}
```

> `AppSettingsDto.Default` is the existing default settings factory used elsewhere in tests; if the symbol differs, mirror however `ISettingsStore` is faked in `SettingsViewModel`/handler tests. Confirm the exact `AiBackend`/`AppSettingsDto` member names by reading `src/AIHelperNET.Application/Abstractions/ISettingsStore.cs` and the `AppSettingsDto` definition before running.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateAnswerHandlerTests"`
Expected: FAIL — `GenerateAnswerHandler` has no 7th constructor parameter (`feedback`), so it won't compile.

- [ ] **Step 3: Inject the feedback channel into the handler**

In `GenerateAnswerCommand.cs`, add `ITurnStatusFeedback feedback` to the primary constructor (lines 21-27):

```csharp
public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ITurnStatusFeedback feedback) : IRequestHandler<GenerateAnswerCommand, Result>
```

- [ ] **Step 4: Publish on each transition**

In `Handle`, after the generating transition (line 77 `turn.TransitionTo(genStatus);`) add:

```csharp
        feedback.Publish(new TurnStatusEvent(cmd.TurnId, genStatus));
```

After the ready transition (line 106 `turn.TransitionTo(readyStatus);`) add:

```csharp
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, readyStatus));
```

In the `catch (OperationCanceledException)` block (line 110-113), publish the turn's resulting status so the pipeline learns the generation ended. After `answer.Cancel(clock.GetUtcNow());` add:

```csharp
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, turn.Status));
```

In the `catch (Exception ex)` block (line 115-119), after `answer.Fail(clock.GetUtcNow());` add:

```csharp
            feedback.Publish(new TurnStatusEvent(cmd.TurnId, turn.Status));
```

> On cancel/fail the turn's `Status` is still the `Generating*` value (no terminal transition happens in those paths today). Publishing it is harmless — the pipeline applies it idempotently and disposes nothing (Generating* is not in the ready/terminal dispose set). This keeps the contract "every transition the handler performs is published" honest without inventing new statuses.

- [ ] **Step 5: Remove `repository.Update(session)`**

Replace the final two lines (lines 122-123):

```csharp
        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(cancellationToken);
```

with:

```csharp
        // No repository.Update(session): rely on EF change-tracking so only the entities this
        // handler actually modified (turn Status, the new AnswerVersion, the GeneratedAnswer) are
        // written. A full-graph Update would mark pipeline-owned columns (clarification IDs,
        // pre-answer status) Modified and clobber them. See Spec 1 §5.5.
        return await unitOfWork.SaveChangesAsync(cancellationToken);
```

Add the using at the top of `GenerateAnswerCommand.cs` if not already present (it already imports `AIHelperNET.Application.Abstractions`, so `ITurnStatusFeedback`/`TurnStatusEvent` resolve without a new using).

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateAnswerHandlerTests"`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs \
        tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs
git commit -m "feat: answer handler publishes turn status, drops full-graph repository.Update"
```

---

## Task 7: Fold clarification context into the regenerated prompt

For the `Me`-clarification flow to actually change the answer (spec §3 / §10 acceptance "regenerates incorporating it"), the handler must include the turn's clarification transcript items in the prompt. Today `recentTranscript` filters `Timestamp <= turn.CreatedAt`, so clarifications added *after* the turn was created are excluded.

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`
- Test: `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `GenerateAnswerHandlerTests.cs`. It captures the prompt handed to the provider and asserts the clarification text is present:

```csharp
    [Fact]
    public async Task Handle_IncludesClarificationTranscriptItemsInPrompt()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "Explain DI.", T0, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;

        // A candidate clarification recorded AFTER the turn was created.
        var clarification = TranscriptItem.Create(
            Speaker.Me, "Do you mean constructor injection specifically?", T0.AddSeconds(5), 0.9f);
        session.AddTranscriptItem(clarification);
        turn.AttachClarificationQuestion(clarification.Id);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));

        AnswerPrompt? captured = null;
        var provider = Substitute.For<IAnswerProvider>();
        provider.StreamAnswerAsync(Arg.Any<AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(ci => { captured = ci.ArgAt<AnswerPrompt>(0); return Stream("ok"); });
        var resolver = Substitute.For<IAnswerProviderResolver>();
        resolver.Resolve(Arg.Any<AiBackend>()).Returns(provider);

        var settings = Substitute.For<ISettingsStore>();
        settings.LoadAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(AppSettingsDto.Default));
        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateAnswerHandler(
            repo, resolver, settings, streamSink, uow, TimeProvider.System, new TurnStatusFeedback());

        await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.RefinedAfterClarification),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.User.Should().Contain("constructor injection specifically");
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~IncludesClarificationTranscriptItemsInPrompt"`
Expected: FAIL — the clarification text is not in the prompt (it is filtered out by `Timestamp <= turn.CreatedAt`).

- [ ] **Step 3: Union clarification items into `recentTranscript`**

In `GenerateAnswerCommand.cs`, replace the `recentTranscript` construction (lines 52-56):

```csharp
        // Collect last 5 transcript items up to (and including) when this turn was created.
        var recentTranscript = session.Transcript
            .Where(t => t.Timestamp <= turn.CreatedAt)
            .TakeLast(5)
            .ToList();
```

with:

```csharp
        // Last 5 transcript items up to (and including) when this turn was created…
        var preTurn = session.Transcript
            .Where(t => t.Timestamp <= turn.CreatedAt)
            .TakeLast(5);

        // …plus any clarification items recorded on this turn AFTER it was created (e.g. a Me
        // clarification). These post-creation items are what makes a regenerated answer actually
        // incorporate the clarification. See Spec 1 §3.
        var clarificationIds = turn.ClarificationQuestionIds
            .Concat(turn.ClarificationResponseIds)
            .ToHashSet();
        var clarifications = session.Transcript.Where(t => clarificationIds.Contains(t.Id));

        var recentTranscript = preTurn
            .Concat(clarifications)
            .DistinctBy(t => t.Id)
            .OrderBy(t => t.Timestamp)
            .ToList();
```

> `DistinctBy` is in `System.Linq` (.NET 6+). No new using needed — the file already builds LINQ queries.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~IncludesClarificationTranscriptItemsInPrompt"`
Expected: PASS.

- [ ] **Step 5: Run both handler tests together**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateAnswerHandlerTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs \
        tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs
git commit -m "feat: include turn clarification items in the (re)generated prompt"
```

---

## Task 8: Integration test — persistence partition keeps clarification IDs intact

Prove the root-cause fix end-to-end against real EF/SQLite: after the pipeline writes clarification IDs and the handler later saves (without `repository.Update`), the clarification IDs survive.

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/Persistence/SessionPersistenceTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `SessionPersistenceTests.cs` (the class already exposes `_repo` and `_db` via `IAsyncLifetime`):

```csharp
    [Fact]
    public async Task HandlerSave_AfterPipelineWroteClarificationIds_DoesNotClobberThem()
    {
        // Pipeline context: create a turn and attach a clarification id, then persist.
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        var item = TranscriptItem.Create(Speaker.Me, "Constructor injection?", DateTimeOffset.UtcNow, 0.9f);
        session.AddTranscriptItem(item);
        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, DateTimeOffset.UtcNow);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", DateTimeOffset.UtcNow).Value;
        turn.AttachClarificationQuestion(item.Id);
        turn.TransitionTo(ConversationTurnStatus.AwaitingClarification);

        await _repo.AddAsync(session, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        // Answer-handler context (separate load): advance status + add an answer version, save WITHOUT Update().
        var reload = (await _repo.GetAsync(session.Id, default)).Value;
        var t2 = reload.ConversationTurns.Single();
        t2.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
        t2.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.Preliminary, "DI is...", DateTimeOffset.UtcNow));
        t2.TransitionTo(ConversationTurnStatus.PreliminaryReady);
        await _db.SaveChangesAsync(); // EF change-tracking only — no repository.Update
        _db.ChangeTracker.Clear();

        // Verify: status advanced AND the clarification id the pipeline wrote is still present.
        var final = (await _repo.GetAsync(session.Id, default)).Value;
        var ft = final.ConversationTurns.Single();
        ft.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        ft.AnswerVersions.Should().HaveCount(1);
        ft.ClarificationQuestionIds.Should().Contain(item.Id);
    }
```

> Add any missing usings at the top of the file: `using AIHelperNET.Domain.Questions;` (for `DetectedQuestion`/`QuestionSource`) and `using AIHelperNET.Domain.Ids;` if needed. `AnswerVersion`/`AnswerVersionType` live in `AIHelperNET.Domain.Sessions` (already imported).

- [ ] **Step 2: Run test to verify it passes (this is a guard, not red→green)**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~HandlerSave_AfterPipelineWroteClarificationIds"`
Expected: PASS. This test encodes the contract Task 6 established (no `repository.Update`); it documents and guards the partition. If it FAILS (clarification IDs missing), it means an `Update`/full-graph save is still clobbering — investigate before proceeding.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Persistence/SessionPersistenceTests.cs
git commit -m "test: persistence partition keeps clarification IDs intact across handler save"
```

---

## Task 9: Full verification + branch wrap-up

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution (warnings are errors here)**

Run: `dotnet build`
Expected: Build succeeded, 0 errors, 0 warnings. (`Directory.Build.props` sets `TreatWarningsAsErrors`.)

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test`
Expected: All green. Pay attention to the previously-passing `TranscriptPipelineServiceTests` count — it should grow by the new tests and lose none.

- [ ] **Step 3: Confirm no EF schema change slipped in**

This spec adds no entity columns (`TurnStatusEvent` is in-memory only; clarification IDs already persist). Confirm:

Run: `git diff --name-only origin/develop -- src/AIHelperNET.Infrastructure/Persistence`
Expected: empty output. If anything under `Persistence/` changed, run the `add-ef-migration` skill before merging (per CLAUDE.md).

- [ ] **Step 4: E2E (manual, from `develop`, system audio)**

Per CLAUDE.md and the spec §10, run the `e2e-test` skill. Specifically verify:
1. Two distinct interviewer (`Other`) questions in a row → **two** answer cards, each with its own answer (no skipping, no cross-cancel).
2. A candidate (`Me`) clarification on an unanswered question → the card waits, and the next interviewer line regenerates the answer **incorporating** the clarification.

Use **system audio**, not mic-only (the `Me`/`Other` split needs both loopback and mic — see `[[reference-conversation-routing-model]]`).

- [ ] **Step 5: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to open a PR from `feature/conversation-core` → `develop`. PR description should note it supersedes PR #14's `Me`-path (commit `6d20880`) with deterministic routing while keeping the Other-path hardening (`c769753`) and `BoundaryRoute` instrumentation.

---

## Self-Review notes (for the executor)

- **Spec coverage:** §3 routing → Tasks 5 (Me) + 3/4 (Other accuracy); §4 feedback architecture → Tasks 1, 4, 6; §5.1 port → Task 1; §5.2 handler → Task 6; §5.3 pipeline → Tasks 3, 4, 5; §5.4 `LastTurn` → Task 2; §5.5 partition → Tasks 6 + 8; §10 testing → Tasks 1-8; "incorporating clarification" acceptance → Task 7.
- **Out of scope (do not touch):** endpointing/segmentation (Spec 2), boundary-classifier prompt/observability (Spec 3), any UI/overlay/sink contract change.
- **Type consistency:** `ITurnStatusFeedback` methods are `Publish(TurnStatusEvent)` / `bool TryDrain(out TurnStatusEvent)` everywhere; `TurnStatusEvent(ConversationTurnId TurnId, ConversationTurnStatus Status)`; the per-turn registry is `_turnCts` (`ConcurrentDictionary<ConversationTurnId, CancellationTokenSource>`); the pipeline's new method is `HandleMeUtterance`; the drain method is `DrainStatusFeedback`.
- **Known soft decision (spec §3, carried forward):** the answered-turn `Me` follow-up records context only, no auto-regenerate. Task 5's second test pins this behaviour — if product later wants auto-regenerate, change that test and `HandleMeUtterance` together.
