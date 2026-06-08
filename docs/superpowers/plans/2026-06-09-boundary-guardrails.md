# Boundary-classifier Guardrails + Decision Observability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop a single `NewQuestion` mislabel from dismissing a live, recently-active turn (the "over-split"), and durably record every boundary-routing decision as JSONL.

**Architecture:** A pure `BoundarySplitGuard` decides Split vs Append from (effective confidence, live-turn, recency). A pure `SplitConfidence` helper demotes confidence when the heuristic and AI disagree about splitting. The pipeline tracks per-turn last-activity via its injected `TimeProvider`, consults the guard only at the destructive `NewQuestion` branch, and emits a `BoundaryDecisionRecord` through an `IBoundaryDecisionRecorder` port whose Infrastructure impl appends JSON lines under the data root.

**Tech Stack:** .NET 10, C#, xUnit + FluentAssertions + NSubstitute + `Microsoft.Extensions.Time.Testing` (`FakeTimeProvider`), System.Text.Json, Serilog.

---

## File Structure

**Create:**
- `src/AIHelperNET.Application/Sessions/BoundarySplitGuard.cs` — pure guard + `SplitDecision` enum.
- `src/AIHelperNET.Application/Sessions/SplitConfidence.cs` — pure agreement/demotion helper.
- `src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs` — port + `BoundaryDecisionRecord`.
- `src/AIHelperNET.Infrastructure/Diagnostics/JsonlBoundaryDecisionRecorder.cs` — best-effort JSONL writer.
- `tests/AIHelperNET.Application.Tests/Sessions/BoundarySplitGuardTests.cs`
- `tests/AIHelperNET.Application.Tests/Sessions/SplitConfidenceTests.cs`
- `tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs`

**Modify:**
- `src/AIHelperNET.Infrastructure/Common/AppPaths.cs` — add `DiagnosticsDir` + create it.
- `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` — recency dict, guard call, effective-confidence, recorder call, enriched log.
- `src/AIHelperNET.Infrastructure/DependencyInjection.cs` — register the recorder.
- `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` — new guard/recency/recorder pipeline tests.

---

## Task 1: `BoundarySplitGuard` (pure decision)

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/BoundarySplitGuard.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/BoundarySplitGuardTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Application.Tests/Sessions/BoundarySplitGuardTests.cs`:

```csharp
using AIHelperNET.Application.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class BoundarySplitGuardTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(BoundarySplitGuard.RecencyWindowSeconds);
    private static readonly TimeSpan WithinWindow = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan PastWindow = TimeSpan.FromSeconds(BoundarySplitGuard.RecencyWindowSeconds + 1);

    private readonly BoundarySplitGuard _guard = new();

    [Fact]
    public void NoLiveTurn_AlwaysSplits()
    {
        _guard.Evaluate(effectiveConfidence: 0.10, hasLiveTurn: false, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_PastRecencyWindow_Splits_EvenAtLowConfidence()
    {
        _guard.Evaluate(0.10, hasLiveTurn: true, sinceLastActivity: PastWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_Recent_HighConfidence_Splits()
    {
        _guard.Evaluate(BoundarySplitGuard.SplitConfidenceBar, hasLiveTurn: true, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.Split);
    }

    [Fact]
    public void LiveTurn_Recent_LowConfidence_Appends()
    {
        _guard.Evaluate(0.80, hasLiveTurn: true, sinceLastActivity: WithinWindow)
            .Should().Be(SplitDecision.AppendToActiveTurn);
    }

    [Fact]
    public void LiveTurn_ExactlyAtWindowBoundary_TreatedAsRecent()
    {
        // sinceLastActivity == window is NOT past the window → recent → needs the bar
        _guard.Evaluate(0.80, hasLiveTurn: true, sinceLastActivity: Window)
            .Should().Be(SplitDecision.AppendToActiveTurn);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BoundarySplitGuard"`
Expected: FAIL — `BoundarySplitGuard` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/AIHelperNET.Application/Sessions/BoundarySplitGuard.cs`:

```csharp
namespace AIHelperNET.Application.Sessions;

/// <summary>The action to take for a candidate <c>NewQuestion</c> split.</summary>
public enum SplitDecision
{
    /// <summary>Dismiss-and-open: treat the segment as a genuinely new question.</summary>
    Split,

    /// <summary>Suppress the split: append the segment to the live turn instead.</summary>
    AppendToActiveTurn
}

/// <summary>
/// Pure guard protecting the destructive <c>NewQuestion</c> split (which dismisses a live turn and
/// opens a new card). Composes the recency, asymmetric-confidence, and (via the supplied effective
/// confidence) heuristic/AI-agreement guards. No I/O, no clock — deterministically unit-testable.
/// </summary>
public sealed class BoundarySplitGuard
{
    /// <summary>Seconds since the live turn's last activity, within which a split needs high confidence.</summary>
    public const double RecencyWindowSeconds = 6.0;

    /// <summary>Effective-confidence required to split a recent, live turn.</summary>
    public const double SplitConfidenceBar = 0.90;

    /// <summary>
    /// Decides whether a <c>NewQuestion</c> should split off a new turn or append to the live one.
    /// </summary>
    /// <param name="effectiveConfidence">Confidence after any agreement demotion (see <see cref="SplitConfidence"/>).</param>
    /// <param name="hasLiveTurn">Whether a non-terminal active turn exists to protect.</param>
    /// <param name="sinceLastActivity">Elapsed time since that turn was last active.</param>
    public SplitDecision Evaluate(double effectiveConfidence, bool hasLiveTurn, TimeSpan sinceLastActivity)
    {
        if (!hasLiveTurn)
            return SplitDecision.Split;

        if (sinceLastActivity > TimeSpan.FromSeconds(RecencyWindowSeconds))
            return SplitDecision.Split;

        return effectiveConfidence >= SplitConfidenceBar
            ? SplitDecision.Split
            : SplitDecision.AppendToActiveTurn;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~BoundarySplitGuard"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/BoundarySplitGuard.cs tests/AIHelperNET.Application.Tests/Sessions/BoundarySplitGuardTests.cs
git commit -m "feat(pipeline): pure BoundarySplitGuard for NewQuestion splits"
```

---

## Task 2: `SplitConfidence` (agreement demotion)

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/SplitConfidence.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/SplitConfidenceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Application.Tests/Sessions/SplitConfidenceTests.cs`:

```csharp
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SplitConfidenceTests
{
    [Fact]
    public void NoOtherOpinion_KeepsConfidence_AndAgrees()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.85, otherLabel: null);

        effective.Should().Be(0.85);
        agreed.Should().BeTrue();
    }

    [Fact]
    public void BothSayNewQuestion_NoDemotion()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.85, otherLabel: BoundaryLabel.NewQuestion);

        effective.Should().Be(0.85);
        agreed.Should().BeTrue();
    }

    [Theory]
    [InlineData(BoundaryLabel.QuestionContinued)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion)]
    [InlineData(BoundaryLabel.AdditionalRequirement)]
    public void FinalNewQuestion_OtherIsContinuationFamily_Demotes(BoundaryLabel other)
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.90, otherLabel: other);

        effective.Should().Be(0.90 * SplitConfidence.DisagreementPenalty);
        agreed.Should().BeFalse();
    }

    [Fact]
    public void FinalContinuation_OtherIsNewQuestion_AlsoDemotes_Symmetric()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.QuestionContinued, 0.90, otherLabel: BoundaryLabel.NewQuestion);

        effective.Should().Be(0.90 * SplitConfidence.DisagreementPenalty);
        agreed.Should().BeFalse();
    }

    [Fact]
    public void UnrelatedDisagreement_NotAboutSplitting_NoDemotion()
    {
        // Neither side is NewQuestion → not a split disagreement → no demotion.
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.QuestionComplete, 0.80, otherLabel: BoundaryLabel.QuestionContinued);

        effective.Should().Be(0.80);
        agreed.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SplitConfidence"`
Expected: FAIL — `SplitConfidence` does not exist.

- [ ] **Step 3: Write the implementation**

Create `src/AIHelperNET.Application/Sessions/SplitConfidence.cs`:

```csharp
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Computes the effective confidence for a split decision, demoting it when the fast heuristic and
/// the AI classifier disagree about whether a segment is a new question (the "agreement" guard).
/// Pure and side-effect free.
/// </summary>
public static class SplitConfidence
{
    /// <summary>Multiplier applied to confidence when the two opinions disagree on splitting.</summary>
    public const double DisagreementPenalty = 0.5;

    /// <summary>Continuation-family labels: non-split labels that contradict a <c>NewQuestion</c>.</summary>
    public static bool IsContinuationFamily(BoundaryLabel label) =>
        label is BoundaryLabel.QuestionContinued
              or BoundaryLabel.ClarificationOfCurrentQuestion
              or BoundaryLabel.AdditionalRequirement;

    /// <summary>
    /// Returns the effective confidence and whether the two opinions agree about splitting.
    /// </summary>
    /// <param name="finalLabel">The label that would drive routing.</param>
    /// <param name="finalConfidence">Its reported confidence.</param>
    /// <param name="otherLabel">The other opinion (heuristic vs AI), or <see langword="null"/> if only one exists.</param>
    public static (double Effective, bool Agreed) Resolve(
        BoundaryLabel finalLabel, double finalConfidence, BoundaryLabel? otherLabel)
    {
        if (otherLabel is null)
            return (finalConfidence, true);

        var disagreeOnSplit =
            (finalLabel == BoundaryLabel.NewQuestion && IsContinuationFamily(otherLabel.Value)) ||
            (otherLabel.Value == BoundaryLabel.NewQuestion && IsContinuationFamily(finalLabel));

        return disagreeOnSplit
            ? (finalConfidence * DisagreementPenalty, false)
            : (finalConfidence, true);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SplitConfidence"`
Expected: PASS (7 cases).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/SplitConfidence.cs tests/AIHelperNET.Application.Tests/Sessions/SplitConfidenceTests.cs
git commit -m "feat(pipeline): SplitConfidence agreement-demotion helper"
```

---

## Task 3: `IBoundaryDecisionRecorder` port + `BoundaryDecisionRecord`

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs`

This task defines only the port and DTO (no behavior), so it has no standalone test; it is exercised by Tasks 4 and 5.

- [ ] **Step 1: Write the port and record**

Create `src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs`:

```csharp
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// A single boundary-routing decision: both classifier opinions, the agreement outcome, the guard
/// result, and the final route. Durable so it can be inspected and replayed (Spec 3b corpus).
/// </summary>
public sealed record BoundaryDecisionRecord(
    DateTimeOffset Timestamp,
    Guid SessionId,
    Guid? TurnId,
    Speaker Speaker,
    ConversationTurnStatus? StaleTurnStatus,
    BoundaryLabel HeuristicLabel,
    double HeuristicConfidence,
    BoundaryLabel? AiLabel,
    double? AiConfidence,
    bool Agreed,
    double EffectiveConfidence,
    string Route,
    BoundaryLabel FinalLabel,
    string TextClip);

/// <summary>Port that durably records each boundary-routing decision. Implementations must be best-effort.</summary>
public interface IBoundaryDecisionRecorder
{
    /// <summary>Records one decision. Implementations must never throw — recording is diagnostic only.</summary>
    void Record(BoundaryDecisionRecord record);
}
```

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: Build succeeded, 0 warnings.

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs
git commit -m "feat(abstractions): IBoundaryDecisionRecorder port + BoundaryDecisionRecord"
```

---

## Task 4: `JsonlBoundaryDecisionRecorder` + `AppPaths.DiagnosticsDir`

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Common/AppPaths.cs`
- Create: `src/AIHelperNET.Infrastructure/Diagnostics/JsonlBoundaryDecisionRecorder.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs`

- [ ] **Step 1: Add `DiagnosticsDir` to `AppPaths`**

In `src/AIHelperNET.Infrastructure/Common/AppPaths.cs`, add the property next to the other paths (after `LogFile`):

```csharp
    public static string DiagnosticsDir => Path.Combine(Base, "diagnostics");
```

And in `EnsureDirectoriesExist()`, add a create call alongside the others:

```csharp
        Directory.CreateDirectory(DiagnosticsDir);
```

- [ ] **Step 2: Write the failing tests**

Create `tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs`:

```csharp
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Diagnostics;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Diagnostics;

public class JsonlBoundaryDecisionRecorderTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "aih-diag-" + Guid.NewGuid().ToString("N"));

    private static BoundaryDecisionRecord Sample() => new(
        Timestamp: DateTimeOffset.UnixEpoch,
        SessionId: Guid.NewGuid(),
        TurnId: Guid.NewGuid(),
        Speaker: Speaker.Other,
        StaleTurnStatus: ConversationTurnStatus.PreliminaryReady,
        HeuristicLabel: BoundaryLabel.QuestionContinued,
        HeuristicConfidence: 0.30,
        AiLabel: BoundaryLabel.NewQuestion,
        AiConfidence: 0.80,
        Agreed: false,
        EffectiveConfidence: 0.40,
        Route: "AppendToActiveTurn",
        FinalLabel: BoundaryLabel.NewQuestion,
        TextClip: "how would you handle invalidation");

    [Fact]
    public void Record_WritesOneJsonLinePerCall()
    {
        var recorder = new JsonlBoundaryDecisionRecorder(_dir);

        recorder.Record(Sample());
        recorder.Record(Sample());

        var file = Directory.GetFiles(_dir, "boundary-decisions-*.jsonl").Single();
        var lines = File.ReadAllLines(file);
        lines.Should().HaveCount(2);

        // each line is valid JSON with enums written as readable strings
        using var doc = JsonDocument.Parse(lines[0]);
        doc.RootElement.GetProperty("FinalLabel").GetString().Should().Be("NewQuestion");
        doc.RootElement.GetProperty("Route").GetString().Should().Be("AppendToActiveTurn");
    }

    [Fact]
    public void Record_DoesNotThrow_WhenDirectoryPathIsInvalid()
    {
        // A path containing an invalid character can't be created/written — must be swallowed.
        var recorder = new JsonlBoundaryDecisionRecorder("\0:invalid");

        var act = () => recorder.Record(Sample());

        act.Should().NotThrow();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~JsonlBoundaryDecisionRecorder"`
Expected: FAIL — `JsonlBoundaryDecisionRecorder` does not exist.

- [ ] **Step 4: Write the implementation**

Create `src/AIHelperNET.Infrastructure/Diagnostics/JsonlBoundaryDecisionRecorder.cs`:

```csharp
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHelperNET.Application.Abstractions;
using Serilog;

namespace AIHelperNET.Infrastructure.Diagnostics;

/// <summary>
/// Best-effort <see cref="IBoundaryDecisionRecorder"/> that appends one JSON line per decision to a
/// dated file under the diagnostics directory. All I/O failures are swallowed (logged at Debug) so
/// recording never disturbs the transcript pipeline. Appends are serialized by a lock to keep lines
/// intact across threads.
/// </summary>
public sealed class JsonlBoundaryDecisionRecorder(string directory) : IBoundaryDecisionRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    /// <inheritdoc/>
    public void Record(BoundaryDecisionRecord record)
    {
        try
        {
            var file = Path.Combine(
                directory, $"boundary-decisions-{record.Timestamp:yyyy-MM-dd}.jsonl");
            var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(file, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "JsonlBoundaryDecisionRecorder: failed to record decision");
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~JsonlBoundaryDecisionRecorder"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Common/AppPaths.cs src/AIHelperNET.Infrastructure/Diagnostics/JsonlBoundaryDecisionRecorder.cs tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs
git commit -m "feat(diagnostics): JSONL boundary-decision recorder + AppPaths.DiagnosticsDir"
```

---

## Task 5: Wire guard + recency + recorder into the pipeline

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

This task integrates the pure components from Tasks 1–4. Implement the production code first (the new constructor parameter and fields are needed for the test helper to compile), then add the tests.

- [ ] **Step 1: Add the recorder parameter, time field, recency dict, and guard**

In `TranscriptPipelineService.cs`, change the primary constructor to add a trailing optional parameter:

```csharp
public sealed partial class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink,
    IQuestionClassifier classifier,
    ILogger<TranscriptPipelineService>? logger = null,
    IQuestionBoundaryClassifier? boundaryClassifier = null,
    ITurnStatusFeedback? feedback = null,
    TimeProvider? timeProvider = null,
    IBoundaryDecisionRecorder? recorder = null) : IDisposable
```

In the field block near the top, leave the existing `_regenDebouncer` line untouched and add three new fields next to it, so the block reads:

```csharp
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly RegenDebouncer _regenDebouncer = new(timeProvider ?? TimeProvider.System);
    private readonly BoundarySplitGuard _splitGuard = new();
    private readonly ConcurrentDictionary<ConversationTurnId, DateTimeOffset> _lastActivityAt = new();
```

(`_time` and the debouncer both fall back to `TimeProvider.System`, so they share one clock.)

- [ ] **Step 2: Add the recency helper and the post-routing stamp**

Add these private members to the class (near `IsTerminal`):

```csharp
    private DateTimeOffset LastActivity(ConversationTurnId id) =>
        _lastActivityAt.TryGetValue(id, out var t) ? t : DateTimeOffset.MinValue;

    private void StampActivity(ConversationTurnId id) => _lastActivityAt[id] = _time.GetUtcNow();
```

- [ ] **Step 3: Capture both opinions, then compute the guard + record in `BuildCommandWithBoundaryAsync`**

Replace the region from the `// Heuristic first` comment through the final `return RouteLabel(session, item, result, activeTurn);` line with the version below. The key change vs the original is naming the first evaluation `heuristic` (kept) and capturing the AI's opinion explicitly in `aiLabel`/`aiConfidence` — so `otherOpinion` is non-null **only when the AI actually ran**. (Do NOT re-evaluate the detector to recover the heuristic: it returns a fresh object each call, so a reference compare would misfire when the AI was never called.)

```csharp
        // Heuristic first — kept as `heuristic` so its opinion survives any AI replacement.
        var heuristic = _boundaryDetector.Evaluate(item.Text, item.Speaker, activeTurnStatus, recentTexts);
        var result = heuristic;
        BoundaryLabel? aiLabel = null;
        double? aiConfidence = null;

        // If ambiguous (confidence < 0.7), call AI classifier
        if (result.Confidence < 0.7)
        {
            try
            {
                result = await boundaryClassifier!.ClassifyAsync(
                    activeTurnStatus, _recentItems.AsReadOnly(), item, item.Speaker, ct);
                aiLabel = result.Classification;
                aiConfidence = result.Confidence;
            }
            catch (Exception ex)
            {
                if (logger is not null)
                    Log.BoundaryClassifierFailed(logger, ex, item.Text[..Math.Min(80, item.Text.Length)]);
            }

            // Safety net: bias-free heuristic re-check (no active-turn context). See Spec 1/2.
            if (result.Confidence < 0.7
                && activeTurnStatus != ConversationTurnStatus.CollectingQuestion)
            {
                var neutral = _boundaryDetector.Evaluate(item.Text, item.Speaker, null, recentTexts);
                if (neutral.Classification is BoundaryLabel.QuestionComplete
                                           or BoundaryLabel.TaskComplete
                                           or BoundaryLabel.NewQuestion)
                {
                    result = neutral;
                }
            }
        }

        // The "other opinion" for agreement is the heuristic — only meaningful once the AI ran.
        var otherOpinion = aiLabel is not null ? heuristic.Classification : (BoundaryLabel?)null;

        item.SetBoundaryRole(LabelToRole(result.Classification));

        // Guard only the destructive NewQuestion split; everything else routes unchanged.
        var (effectiveConfidence, agreed) =
            SplitConfidence.Resolve(result.Classification, result.Confidence, otherOpinion);
        SplitDecision? guard = null;
        if (result.Classification == BoundaryLabel.NewQuestion)
        {
            var live = activeTurn is not null && !IsTerminal(activeTurn.Status);
            var since = live ? _time.GetUtcNow() - LastActivity(activeTurn!.Id) : TimeSpan.MaxValue;
            guard = _splitGuard.Evaluate(effectiveConfidence, live, since);
        }

        var cmd = RouteLabel(session, item, result, activeTurn, guard);

        // Stamp recency for whichever turn is now active (covers create/append/fragment cases).
        if (session.ActiveTurn is { } nowActive)
            StampActivity(nowActive.Id);

        if (logger is not null)
            Log.BoundaryRouted(logger, item.Speaker, activeTurnStatus,
                heuristic.Classification, heuristic.Confidence, aiLabel, aiConfidence,
                agreed, effectiveConfidence, guard?.ToString() ?? "-",
                result.Classification, result.Confidence,
                item.Text[..Math.Min(60, item.Text.Length)]);

        recorder?.Record(new BoundaryDecisionRecord(
            Timestamp: _time.GetUtcNow(),
            SessionId: session.Id.Value,
            TurnId: session.ActiveTurn?.Id.Value,
            Speaker: item.Speaker,
            StaleTurnStatus: activeTurnStatus,
            HeuristicLabel: heuristic.Classification,
            HeuristicConfidence: heuristic.Confidence,
            AiLabel: aiLabel,
            AiConfidence: aiConfidence,
            Agreed: agreed,
            EffectiveConfidence: effectiveConfidence,
            Route: guard?.ToString() ?? result.Classification.ToString(),
            FinalLabel: result.Classification,
            TextClip: item.Text[..Math.Min(120, item.Text.Length)]));

        return cmd;
```

NOTE: `activeTurn`, `activeTurnStatus`, and `recentTexts` are declared earlier in the method (above the `// Heuristic first` comment) and are unchanged. This replacement only restructures from `// Heuristic first` onward.

- [ ] **Step 4: Add the `guard` parameter to `RouteLabel` and guard the NewQuestion branch**

Change the `RouteLabel` signature and its `NewQuestion` case:

```csharp
    private GenerateAnswerCommand? RouteLabel(
        Session session, TranscriptItem item, BoundaryClassificationResult result,
        ConversationTurn? activeTurn, SplitDecision? splitGuard)
    {
        switch (result.Classification)
        {
            // ... all other cases unchanged ...

            case BoundaryLabel.NewQuestion:
                if (splitGuard == SplitDecision.AppendToActiveTurn)
                    return AppendGuardedSplit(session, item, activeTurn);
                if (activeTurn?.Status == ConversationTurnStatus.CollectingQuestion)
                    activeTurn.Dismiss();
                return HandleNewQuestion(session, item.Text, item.Timestamp);

            default: // NoQuestion, Unrelated
                return null;
        }
    }
```

Add the new helper (next to `AppendContinuation`), mirroring the `QuestionContinued` semantics — fragment while collecting, otherwise append + debounced regen:

```csharp
    /// <summary>
    /// A <c>NewQuestion</c> the guard demoted to a continuation of the live turn. Mirrors
    /// <see cref="BoundaryLabel.QuestionContinued"/> handling: add a fragment while still collecting,
    /// otherwise append to the question and schedule a coalesced regeneration.
    /// </summary>
    private GenerateAnswerCommand? AppendGuardedSplit(Session session, TranscriptItem item, ConversationTurn? activeTurn)
    {
        if (activeTurn is null || IsTerminal(activeTurn.Status))
            return HandleNewQuestion(session, item.Text, item.Timestamp);

        if (activeTurn.Status == ConversationTurnStatus.CollectingQuestion)
        {
            activeTurn.AddFragment(item.Text);
            return null;
        }

        activeTurn.AppendToQuestion(item.Text);
        ScheduleRegen(session.Id, activeTurn.Id);
        return null;
    }
```

- [ ] **Step 5: Remove the recency entry on terminal/ready in `DrainStatusFeedback`**

In `DrainStatusFeedback`, inside the block that already removes `_turnCts` on ready/terminal, also drop the recency entry. Change:

```csharp
            if (e.Status is ConversationTurnStatus.PreliminaryReady
                          or ConversationTurnStatus.RefinedReady
                          or ConversationTurnStatus.Dismissed
                          or ConversationTurnStatus.Resolved
                && _turnCts.TryRemove(e.TurnId, out var cts))
            {
                cts.Dispose();
            }
```

to additionally clear the activity entry (place right after the `cts.Dispose();` block, still inside the `while`):

```csharp
            if (e.Status is ConversationTurnStatus.Dismissed or ConversationTurnStatus.Resolved)
                _lastActivityAt.TryRemove(e.TurnId, out _);
```

(Leave the existing `_regenDebouncer.Cancel(e.TurnId)` line as-is below it. Activity entries for ready-but-live turns are intentionally kept so recency still applies until the turn goes terminal.)

- [ ] **Step 5b: Enrich the `BoundaryRouted` LoggerMessage to match the new call**

At the bottom of `TranscriptPipelineService.cs`, in the `private static partial class Log`, replace the existing `BoundaryRouted` declaration with the enriched signature (the call in Step 3 already passes these args):

```csharp
        [LoggerMessage(Level = LogLevel.Information,
            Message = "BoundaryRoute: speaker={Speaker} staleStatus={Status} " +
                      "heuristic={Heuristic}({HeuristicConf:F2}) ai={AiLabel}({AiConf}) " +
                      "agreed={Agreed} eff={Effective:F2} guard={Guard} -> {Label} ({Confidence:F2}) text='{Text}'")]
        internal static partial void BoundaryRouted(
            ILogger logger, Speaker speaker, ConversationTurnStatus? status,
            BoundaryLabel heuristic, double heuristicConf, BoundaryLabel? aiLabel, double? aiConf,
            bool agreed, double effective, string guard,
            BoundaryLabel label, double confidence, string text);
```

- [ ] **Step 6: Ensure the necessary usings are present**

At the top of `TranscriptPipelineService.cs`, confirm/add:

```csharp
using AIHelperNET.Application.Abstractions; // already present (ports)
using AIHelperNET.Domain.Questions;         // already present (BoundaryLabel, BoundaryClassificationResult)
```

(`ConcurrentDictionary` via `System.Collections.Concurrent` and `ConversationTurnId` via `AIHelperNET.Domain.Ids` are already imported.)

- [ ] **Step 7: Build to verify the production code compiles**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: Build succeeded, 0 warnings. (Fixes any signature mismatch before touching tests.)

- [ ] **Step 8: Update the test helper to pass a recorder**

In `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`, update `MakeSvcWithBoundary` to accept and pass an optional recorder, returning it for assertions. Replace the helper signature, construction, and return:

```csharp
    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow, ITurnStatusFeedback feedback, IBoundaryDecisionRecorder recorder)
        MakeSvcWithBoundary(ITranscriptSink sink, IQuestionBoundaryClassifier boundaryClassifier,
            TimeProvider? time = null, IBoundaryDecisionRecorder? recorder = null)
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var turnSink = Substitute.For<IConversationTurnSink>();

        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(Result.Ok()));

        var feedback = new TurnStatusFeedback();
        var rec = recorder ?? Substitute.For<IBoundaryDecisionRecorder>();
        return (new TranscriptPipelineService(factory, sink, turnSink,
            Substitute.For<IQuestionClassifier>(), null, boundaryClassifier, feedback, time, rec),
            mediator, turnSink, uow, feedback, rec);
    }
```

Then update the existing call-sites of `MakeSvcWithBoundary` that destructure 5 values to ignore the new 6th. Each existing call like:

```csharp
        var (svc, mediator, _, uow, _) = MakeSvcWithBoundary(transcriptSink, classifier);
```

becomes:

```csharp
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier);
```

(Apply to every `MakeSvcWithBoundary(...)` destructuring in the file — there are several; add one trailing `, _`.)

- [ ] **Step 9: Run the existing boundary tests to verify no regression**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS — all pre-existing boundary tests still green with the new constructor arg and the unchanged routing for non-`NewQuestion` labels.

- [ ] **Step 10: Add the guard behavior tests**

Append these tests to `TranscriptPipelineServiceTests.cs` (inside the class). They drive `NewQuestion` from the AI classifier and assert the guard's append-vs-split outcome. `T0`, `MakeSession`, `MakeItem`, and `FakeTimeProvider` already exist in this file.

```csharp
    private static IQuestionBoundaryClassifier NewQuestionClassifier(double confidence, string normalized)
    {
        var c = Substitute.For<IQuestionBoundaryClassifier>();
        c.ClassifyAsync(
                Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.NewQuestion, confidence,
                ShouldGenerateAnswer: true, ShouldRefineExistingAnswer: false,
                ShouldCreateNewTurn: true, NormalizedQuestionText: normalized, Reason: "test")));
        return c;
    }

    [Fact]
    public async Task Guard_RecentLiveTurn_LowConfidenceNewQuestion_AppendsInsteadOfSplitting()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady); // answered, live

        // AI says NewQuestion at 0.80 (below the 0.90 split bar).
        var classifier = NewQuestionClassifier(0.80, "how would you handle cache invalidation");
        var time = new FakeTimeProvider(T0);
        var (svc, mediator, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        // First, an Other item on this turn establishes recency at T0.
        var first = MakeItem(Speaker.Other, "and what about eviction policies here", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        // 2s later (within the 6s window) the borderline NewQuestion arrives.
        time.Advance(TimeSpan.FromSeconds(2));
        var second = MakeItem(Speaker.Other, "how would you handle cache invalidation", T0.AddSeconds(2));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1, "a recent, low-confidence NewQuestion must append, not split");
        turn.InitialQuestionText.Should().Contain("how would you handle cache invalidation");
    }

    [Fact]
    public async Task Guard_RecentLiveTurn_HighConfidenceNewQuestion_StillSplits()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var classifier = NewQuestionClassifier(0.95, "now tell me about kubernetes networking");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var first = MakeItem(Speaker.Other, "and what about eviction policies here", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        time.Advance(TimeSpan.FromSeconds(2));
        var second = MakeItem(Speaker.Other, "now tell me about kubernetes networking", T0.AddSeconds(2));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "high-confidence NewQuestion clears the split bar even when recent");
    }

    [Fact]
    public async Task Guard_PastRecencyWindow_LowConfidenceNewQuestion_Splits()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain caching strategies.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain caching strategies.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var classifier = NewQuestionClassifier(0.80, "what about kubernetes networking");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time);

        var first = MakeItem(Speaker.Other, "and what about eviction policies here", T0);
        await svc.ProcessAsync(session, first, uow, CancellationToken.None);

        // 7s later — past the 6s window → the prior turn is stale → split.
        time.Advance(TimeSpan.FromSeconds(7));
        var second = MakeItem(Speaker.Other, "what about kubernetes networking", T0.AddSeconds(7));
        await svc.ProcessAsync(session, second, uow, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(2, "a NewQuestion past the recency window splits even at low confidence");
    }

    [Fact]
    public async Task Guard_RecordsDecision_ForEveryOtherItem()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var recorder = Substitute.For<IBoundaryDecisionRecorder>();

        var classifier = NewQuestionClassifier(0.95, "what is dependency injection in dotnet");
        var time = new FakeTimeProvider(T0);
        var (svc, _, _, uow, _, _) = MakeSvcWithBoundary(transcriptSink, classifier, time, recorder);

        var item = MakeItem(Speaker.Other, "what is dependency injection in dotnet", T0);
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        recorder.Received(1).Record(Arg.Is<BoundaryDecisionRecord>(r =>
            r.FinalLabel == BoundaryLabel.NewQuestion && r.SessionId == session.Id.Value));
    }
```

- [ ] **Step 11: Run the new and existing pipeline tests**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"`
Expected: PASS — including the four new `Guard_*` tests.

- [ ] **Step 12: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat(pipeline): guard NewQuestion splits with recency+confidence+agreement; record decisions"
```

---

## Task 6: Register the recorder + full-suite verification

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs`

- [ ] **Step 1: Register the recorder in Infrastructure DI**

In `src/AIHelperNET.Infrastructure/DependencyInjection.cs`, add (next to the other AI registrations, after the `QuestionBoundaryClassifier` lines):

```csharp
        services.AddSingleton<IBoundaryDecisionRecorder>(
            _ => new JsonlBoundaryDecisionRecorder(AppPaths.DiagnosticsDir));
```

Add the usings at the top if not already present:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Diagnostics;
```

- [ ] **Step 2: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 3: Run the full test suite**

Run: `dotnet test`
Expected: PASS except the two long-known UITest failures (`BothMode_MicAndSystemDotsActive`, `ScreenCaptureTests.Capture_WithTestImage_ProducesTurnCard`). No other failures.

- [ ] **Step 4: Close the leftover Photos viewer**

The ScreenCapture UITest spawns a Photos window (`coding_question.png`). Close it before finishing.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs
git commit -m "chore(di): register JsonlBoundaryDecisionRecorder"
```

---

## Self-Review notes (already reconciled)

- **Spec coverage:** recency guard (#1) → Task 1 + Task 5 recency dict; asymmetric confidence (#2) → `SplitConfidenceBar` in Task 1; agreement (#3) → Task 2 + Task 5 effective-confidence; recorder/JSONL → Tasks 3–4 + Task 5 call + Task 6 DI; enriched log → Task 5 (existing `BoundaryRouted`); constants → Tasks 1–2; tradeoff/out-of-scope → documented in spec only (no code).
- **Type consistency:** `BoundarySplitGuard.Evaluate(double, bool, TimeSpan) → SplitDecision`; `SplitConfidence.Resolve(BoundaryLabel, double, BoundaryLabel?) → (double, bool)`; `IBoundaryDecisionRecorder.Record(BoundaryDecisionRecord)`; `RouteLabel(..., SplitDecision?)`; constructor trailing `IBoundaryDecisionRecorder? recorder = null` — used identically in src and the test helper.
- **No placeholders:** every step has full code or an exact command + expected output.

## Notes for the executor

- The new `_lastActivityAt` dictionary is additional pipeline singleton state; it is cleared on terminal in `DrainStatusFeedback` and should also be reset by the deferred per-session-reset follow-up (out of scope here).
- Do not touch the parked `git stash@{0}` or the untracked `.claude/worktrees/...` deletion.
- GitFlow: this work is on `feature/boundary-guardrails`; PR to `develop` at the end via the finishing-a-development-branch skill.
