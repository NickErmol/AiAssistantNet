# Endpointing / Continuation Segmentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop multi-fragment questions (and interviewer follow-ups on an answered turn) from splitting into separate cards, by appending continuation fragments to the same turn and regenerating once, coalesced via a debounce.

**Architecture:** A new `RegenDebouncer` (per-turn timer over `TimeProvider`) coalesces continuation-driven regenerations. `TranscriptPipelineService` routes the "belongs-to-current-turn" boundary labels (`QuestionContinued`, `AdditionalRequirement`, and interviewer `ClarificationOfCurrentQuestion` on an answered turn) into append-text + debounced-regen on the same turn instead of opening a new turn / regenerating immediately. Answer context is widened (`recentQA` 2→3, per-answer clip 200→400) so a mislabeled-as-new follow-up is still answered in context.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute, `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`, already referenced in `AIHelperNET.Application.Tests`).

**Spec:** `docs/superpowers/specs/2026-06-08-endpointing-segmentation-design.md`

---

## File Structure

- `src/AIHelperNET.Application/Sessions/RegenDebouncer.cs` — NEW: per-turn debounce over `TimeProvider`.
- `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` — MODIFY: inject `TimeProvider`, own a `RegenDebouncer`, route continuation labels to append + debounced regen, cleanup.
- `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs` — MODIFY: `recentQA` `TakeLast(2)`→`TakeLast(3)`.
- `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` — MODIFY: per-answer clip `200`→`400` (+ doc comments).
- `tests/AIHelperNET.Application.Tests/Sessions/RegenDebouncerTests.cs` — NEW.
- `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` — MODIFY: helper takes `TimeProvider`; update 4 behavior-changed tests; add continuation tests.
- `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs` — MODIFY: add `recentQA` depth=3 test.
- `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` — MODIFY: add 400-char clip test.

---

## Task 1: `RegenDebouncer`

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/RegenDebouncer.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/RegenDebouncerTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/AIHelperNET.Application.Tests/Sessions/RegenDebouncerTests.cs`:

```csharp
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class RegenDebouncerTests
{
    private static readonly ConversationTurnId Turn = new(Guid.NewGuid());

    [Fact]
    public void BurstOfTouchesWithinWindow_FiresExactlyOnce()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(300));
        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(300));
        sut.Touch(Turn, () => fired++);

        fired.Should().Be(0, "still within the debounce window after each reset");

        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(1, "a burst collapses into a single regeneration");
    }

    [Fact]
    public void TouchesSpacedBeyondWindow_FireForEachQuietGap()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(1);

        sut.Touch(Turn, () => fired++);
        time.Advance(TimeSpan.FromMilliseconds(1000));
        fired.Should().Be(2);
    }

    [Fact]
    public void Cancel_BeforeWindowElapses_NeverFires()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        using var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        sut.Cancel(Turn);
        time.Advance(TimeSpan.FromMilliseconds(2000));

        fired.Should().Be(0);
    }

    [Fact]
    public void Dispose_CancelsPendingTimers()
    {
        var time = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new RegenDebouncer(time);
        var fired = 0;

        sut.Touch(Turn, () => fired++);
        sut.Dispose();
        time.Advance(TimeSpan.FromMilliseconds(2000));

        fired.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~RegenDebouncerTests"`
Expected: FAIL to compile — `RegenDebouncer` does not exist.

- [ ] **Step 3: Implement `RegenDebouncer`**

`src/AIHelperNET.Application/Sessions/RegenDebouncer.cs`:

```csharp
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Per-turn debounce for continuation-driven regenerations. Each <see cref="Touch"/> (re)arms a
/// short timer for the turn; when the quiet window elapses the regeneration callback runs once.
/// A burst of fragments collapses into a single regeneration. Built on <see cref="TimeProvider"/>
/// so it is deterministically testable with <c>FakeTimeProvider</c>.
/// </summary>
public sealed class RegenDebouncer(TimeProvider time) : IDisposable
{
    private const int DebounceMs = 1000;

    private readonly object _gate = new();
    private readonly Dictionary<ConversationTurnId, ITimer> _timers = [];

    /// <summary>(Re)arms the debounce for <paramref name="turnId"/>. On elapse, runs <paramref name="fireRegen"/> once.</summary>
    public void Touch(ConversationTurnId turnId, Action fireRegen)
    {
        lock (_gate)
        {
            if (_timers.TryGetValue(turnId, out var existing))
            {
                existing.Change(TimeSpan.FromMilliseconds(DebounceMs), Timeout.InfiniteTimeSpan);
                return;
            }

            var timer = time.CreateTimer(
                _ =>
                {
                    lock (_gate)
                    {
                        if (_timers.Remove(turnId, out var t)) t.Dispose();
                    }
                    fireRegen();
                },
                state: null,
                dueTime: TimeSpan.FromMilliseconds(DebounceMs),
                period: Timeout.InfiniteTimeSpan);

            _timers[turnId] = timer;
        }
    }

    /// <summary>Drops any pending debounce for a turn (e.g. it went terminal).</summary>
    public void Cancel(ConversationTurnId turnId)
    {
        lock (_gate)
        {
            if (_timers.Remove(turnId, out var t)) t.Dispose();
        }
    }

    /// <summary>Cancels all pending timers.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var t in _timers.Values) t.Dispose();
            _timers.Clear();
        }
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~RegenDebouncerTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/RegenDebouncer.cs tests/AIHelperNET.Application.Tests/Sessions/RegenDebouncerTests.cs
git commit -m "feat(pipeline): add RegenDebouncer for coalesced continuation regeneration"
```

---

## Task 2: Route continuation labels to append + debounced regeneration

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Modify: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

Context: today, after the first answer fires, `RouteLabel`'s `QuestionContinued` falls through to `HandleNewQuestion` when the turn is not `CollectingQuestion`; `AdditionalRequirement` regenerates immediately; and `HandleClarification`'s interviewer branch sends a clarification on an answered turn to `HandleNewQuestion`. Spec 1 made in-memory status accurate, so we now route all three to **append + debounced regen on the same turn**.

- [ ] **Step 1: Add `TimeProvider` + `RegenDebouncer` to the pipeline (no behavior change yet)**

In `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`, add a `TimeProvider` parameter to the primary constructor (last, optional so existing direct constructions still compile) and a `RegenDebouncer` field. Change the constructor signature:

```csharp
public sealed partial class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink,
    IQuestionClassifier classifier,
    ILogger<TranscriptPipelineService>? logger = null,
    IQuestionBoundaryClassifier? boundaryClassifier = null,
    ITurnStatusFeedback? feedback = null,
    TimeProvider? timeProvider = null) : IDisposable
{
```

Add the field near the other fields (after `_turnCts`):

```csharp
    private readonly RegenDebouncer _regenDebouncer = new(timeProvider ?? TimeProvider.System);
```

- [ ] **Step 2: Add the shared helpers**

Add these members to the class (e.g. just above `ForceCompleteCollection`):

```csharp
    private static bool IsTerminal(ConversationTurnStatus status)
        => status is ConversationTurnStatus.Dismissed or ConversationTurnStatus.Resolved;

    /// <summary>
    /// A continuation/clarification fragment for an existing, non-terminal turn: append it to the
    /// turn's question and schedule a coalesced regeneration. With no live turn (or a terminal one)
    /// the fragment is a genuinely new question.
    /// </summary>
    private GenerateAnswerCommand? AppendContinuation(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null || IsTerminal(activeTurn.Status))
            return HandleNewQuestion(session, item.Text, item.Timestamp);

        activeTurn.AppendToQuestion(item.Text);
        ScheduleRegen(session.Id, activeTurn.Id);
        return null;
    }

    private void ScheduleRegen(SessionId sessionId, ConversationTurnId turnId)
        => _regenDebouncer.Touch(turnId, () =>
            FireAndForget(
                new GenerateAnswerCommand(sessionId, turnId, AnswerVersionType.Preliminary),
                CancellationToken.None));
```

- [ ] **Step 3: Re-route the three labels**

In `RouteLabel`, replace the `QuestionContinued` case body:

```csharp
            case BoundaryLabel.QuestionContinued:
                // While still collecting, a continuation is just another fragment.
                if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
                {
                    activeTurn.AddFragment(item.Text);
                    return null;
                }
                // After the first answer has fired, a continuation refines the SAME turn
                // (append + debounced regen) — it does not open a new turn. See Spec 2.
                return AppendContinuation(session, item, activeTurn);
```

Replace `HandleAdditionalRequirement` (make it an instance method, debounced):

```csharp
    private GenerateAnswerCommand? HandleAdditionalRequirement(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null || IsTerminal(activeTurn.Status)) return null;
        activeTurn.AppendToQuestion(item.Text);
        ScheduleRegen(session.Id, activeTurn.Id);
        return null;
    }
```

In `HandleClarification`, replace the interviewer (`else`) branch's "already-complete → new question" block:

```csharp
        else
        {
            // An interviewer "clarification" while we are actually waiting on one → refine now.
            if (activeTurn.Status is ConversationTurnStatus.AwaitingClarification
                                  or ConversationTurnStatus.ClarificationReceived)
            {
                activeTurn.AttachClarificationResponse(item.Id);
                if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
                    activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);
                return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification);
            }

            // Spec 2: an interviewer clarification on an already-answered (non-terminal) turn
            // refines THAT turn (append + debounced regen) instead of splitting off a new card.
            if (!IsTerminal(activeTurn.Status))
            {
                activeTurn.AppendToQuestion(item.Text);
                ScheduleRegen(session.Id, activeTurn.Id);
                return null;
            }

            return HandleNewQuestion(session, item.Text, item.Timestamp);
        }
```

- [ ] **Step 4: Cleanup — cancel debounce on terminal + dispose**

In `DrainStatusFeedback`, after the existing `_turnCts` disposal block, add (inside the `while` loop, after the CTS dispose):

```csharp
            if (e.Status is ConversationTurnStatus.Dismissed or ConversationTurnStatus.Resolved)
                _regenDebouncer.Cancel(e.TurnId);
```

In `Dispose()`, add after `_turnCts.Clear();`:

```csharp
        _regenDebouncer.Dispose();
```

- [ ] **Step 5: Update the test helper to inject `TimeProvider`**

In `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`, change `MakeSvcWithBoundary` to accept an optional `TimeProvider` and pass it last:

```csharp
    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback)
        MakeSvcWithBoundary(ITranscriptSink sink, IQuestionBoundaryClassifier boundaryClassifier, TimeProvider? time = null)
    {
        // ...unchanged setup of mediator/provider/scope/factory/turnSink/uow/feedback...
        var feedback = new TurnStatusFeedback();
        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier, feedback, time),
            mediator, turnSink, uow, feedback);
    }
```

Add the using at the top of the file:

```csharp
using Microsoft.Extensions.Time.Testing;
```

- [ ] **Step 6: Write the new failing tests (coalescing + same-turn refine + terminal)**

> **Test-text rule (applies to every test in this task that forces an AI label):** the pipeline runs its heuristic first and only calls the (mocked) AI classifier when the heuristic confidence is `< 0.7`. So any fragment whose label you want to control via the mock **must be declarative** — no `?`, not imperative, not starting with an interrogative ("how/what/why/can you…") — exactly like the existing `...the system handles concurrent requests efficiently` tests. Otherwise the heuristic answers confidently and your mock is ignored.

Add these tests to `TranscriptPipelineServiceTests.cs`:

```csharp
    [Fact]
    public async Task BoundaryPath_QuestionContinued_AfterFire_BurstOfFragments_CoalescesToOneRegen()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // First "What exactly is DDD?" completes a question (high-confidence heuristic → fires).
        // Subsequent fragments are forced to QuestionContinued via the AI classifier.
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.95,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "frag", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        // Seed an answered turn (PreliminaryReady) the continuations will refine.
        var q = DetectedQuestion.Create("What exactly is DDD?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "What exactly is DDD?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "the system handles concurrent requests efficiently"), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "especially under sustained heavy load"), uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "continuations refine the same turn");
        turn.InitialQuestionText.Should().Contain("especially under sustained heavy load");
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);

        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BoundaryPath_QuestionContinued_TerminalTurn_OpensNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain DI.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain DI.", T0).Value;
        turn.Resolve(); // terminal

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.95,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "x", Reason: "test")));

        var (svc, _, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);

        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "the system handles concurrent requests efficiently", T0.AddSeconds(60)), uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "a continuation of a terminal turn is a new question");
    }
```

- [ ] **Step 7: Update the four behavior-changed tests**

Replace `BoundaryPath_AdditionalRequirement_FiresRefinement` so it advances the debounce before asserting. Replace the method body from the `MakeSvcWithBoundary` call onward:

```csharp
        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, neverCalledClassifier, time);

        var item = MakeItem(Speaker.Other, "Also assume validation errors should not be retried.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
```

Replace `BoundaryPath_FeedbackPreliminaryReady_UnlocksAdditionalRequirementRefine` from the `MakeSvcWithBoundary` call onward (it uses `AdditionalRequirement`, now debounced):

```csharp
        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, feedback) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        // The answer worker reports the turn reached PreliminaryReady.
        feedback.Publish(new TurnStatusEvent(turn.Id, ConversationTurnStatus.PreliminaryReady));

        var item = MakeItem(Speaker.Other, "Also assume it's a web app.");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        // Drain applied → in-memory status is PreliminaryReady, so the requirement refines this turn.
        turn.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id
                && c.VersionType == AnswerVersionType.Preliminary),
            Arg.Any<CancellationToken>());
```

Replace `BoundaryPath_OtherSpeaker_ClassifierReturnsClarification_OnCompletedTurn_CreatesNewTurn` entirely (rename + new contract: refines the same turn):

```csharp
    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ClarificationOnAnsweredTurn_RefinesSameTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.ClarificationOfCurrentQuestion, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "specifically for high-throughput services", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "specifically for high-throughput services", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "an interviewer clarification refines the answered turn, not a new card");
        turn.InitialQuestionText.Should().Contain("specifically for high-throughput services");

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }
```

Replace `BoundaryPath_OtherSpeaker_ClassifierReturnsContinuation_OnCompletedTurn_CreatesNewTurn` entirely (rename + new contract):

```csharp
    [Fact]
    public async Task BoundaryPath_OtherSpeaker_ContinuationOnAnsweredTurn_RefinesSameTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain dependency injection.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain dependency injection.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.85,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "it should also remain testable under heavy load", Reason: "test")));

        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var item = MakeItem(Speaker.Other, "it should also remain testable under heavy load", T0.AddSeconds(60));
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "a continuation refines the answered turn, not a new card");
        turn.InitialQuestionText.Should().Contain("it should also remain testable under heavy load");

        time.Advance(TimeSpan.FromMilliseconds(1100));
        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == turn.Id),
            Arg.Any<CancellationToken>());
    }
```

- [ ] **Step 8: Build and run the pipeline tests**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: 0 warnings/0 errors.

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS — all pipeline tests including the updated four and the two new continuation tests. (The legacy-path test `OtherSpeaker_ClassifierReturnsContinuation_WithActiveTurn_AppendsAndRegenerates` is unaffected — Spec 2 only touches the boundary path.)

- [ ] **Step 9: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat(pipeline): refine same turn on continuation/clarification with debounced regen"
```

---

## Task 3: Widen answer-generation context

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`
- Modify: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`
- Modify: `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs`

- [ ] **Step 1: Write the failing clip test (PromptBuilder 400 chars)**

Add to `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`:

```csharp
    [Fact]
    public void Build_ClipsPriorAnswerContextAt400Chars()
    {
        var longAnswer = new string('A', 500);
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty,
            AnswerSettings.Default,
            "What is DI?",
            screenContext: null,
            recentTranscript: null,
            recentQA: new List<(string, string)> { ("Earlier question?", longAnswer) });

        prompt.User.Should().Contain(new string('A', 400) + "…");
        prompt.User.Should().NotContain(new string('A', 401));
    }
```

(If `PromptBuilderServiceTests` lacks `using AIHelperNET.Domain.ValueObjects;` / `using AIHelperNET.Domain.Sessions;` / `FluentAssertions` / `Xunit`, add them to match the other tests in the file.)

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Build_ClipsPriorAnswerContextAt400Chars"`
Expected: FAIL — current clip is 200, so the prompt contains only `200×A + …`, not `400×A + …`.

- [ ] **Step 3: Raise the clip to 400**

In `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`, in the `recentQA` loop, change:

```csharp
                    var cappedAnswer = a.Length > 200 ? a[..200] + "…" : a;
```
to:
```csharp
                    var cappedAnswer = a.Length > 400 ? a[..400] + "…" : a;
```

Update the two XML doc comments that say "Answers are capped at 200 characters." to "Answers are capped at 400 characters."

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Build_ClipsPriorAnswerContextAt400Chars"`
Expected: PASS.

- [ ] **Step 5: Write the failing depth test (handler includes 3 prior Q&A)**

Add to `tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs`:

```csharp
    [Fact]
    public async Task Handle_IncludesLastThreeAnsweredTurnsAsContext()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

        // Three prior ANSWERED turns (oldest → newest).
        foreach (var (qText, aText, i) in new[]
                 {
                     ("Oldest prior question?", "old answer", 1),
                     ("Middle prior question?", "mid answer", 2),
                     ("Newest prior question?", "new answer", 3),
                 })
        {
            var pq = DetectedQuestion.Create(qText, QuestionSource.Audio, T0.AddSeconds(i));
            session.AddDetectedQuestion(pq);
            var pt = session.AddConversationTurn(pq.Id, qText, T0.AddSeconds(i)).Value;
            pt.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
            pt.AddAnswerVersion(AnswerVersion.Create(AnswerVersionType.Preliminary, aText, T0.AddSeconds(i)));
            pt.TransitionTo(ConversationTurnStatus.PreliminaryReady);
        }

        // Current turn being answered.
        var q = DetectedQuestion.Create("Current question?", QuestionSource.Audio, T0.AddSeconds(10));
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Current question?", T0.AddSeconds(10)).Value;

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
        settings.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AppSettingsDto(
                AiBackend.Claude, WhisperModelSize.Base, AnswerSettings.Default, CodeProfile.Empty,
                MicDeviceId: null, LoopbackDeviceId: null)));
        var streamSink = Substitute.For<IAnswerStreamSink>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateAnswerHandler(
            repo, resolver, settings, streamSink, uow, TimeProvider.System, new TurnStatusFeedback());

        await handler.Handle(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary),
            CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.User.Should().Contain("Oldest prior question?",
            "recentQA now includes the last 3 answered turns, so the oldest of three is present");
    }
```

- [ ] **Step 6: Run to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Handle_IncludesLastThreeAnsweredTurnsAsContext"`
Expected: FAIL — current `TakeLast(2)` excludes the oldest of three.

- [ ] **Step 7: Raise the Q&A depth to 3**

In `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`, in the `recentQA` query, change:

```csharp
            .TakeLast(2)
```
to:
```csharp
            .TakeLast(3)
```

- [ ] **Step 8: Run to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~Handle_IncludesLastThreeAnsweredTurnsAsContext"`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs tests/AIHelperNET.Application.Tests/Answers/GenerateAnswerHandlerTests.cs
git commit -m "feat(answers): widen answer context to last 3 Q&A and 400-char clip"
```

---

## Final verification (before finishing the branch)

- [ ] **Step 1: Clean build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings/0 errors (TreatWarningsAsErrors).

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: All green except the two long-known UITest failures (`BothMode_MicAndSystemDotsActive`, `ScreenCaptureTests.Capture_WithTestImage_ProducesTurnCard`). After the run, close any leftover Photos viewer window (`Get-Process | Where-Object { $_.MainWindowTitle -like '*coding_question*' } | ForEach-Object { $_.CloseMainWindow() }`).

- [ ] **Step 3: Manual smoke (optional, system audio)**

Launch the app (run-aihelper skill); deliver one question in 2–3 chunks via system audio → expect one card whose answer reflects the whole question; an interviewer "can you specify…" after an answer → the same card refines. (Tier-A E2E covers routing deterministically; this validates real audio.)

- [ ] **Step 4: Hand off to finishing-a-development-branch** to open the PR to `develop`.
