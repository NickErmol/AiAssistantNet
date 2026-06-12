# ASR-Degradation Hardening + Eval-as-Guard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stop low-ASR-confidence interviewer speech from being silently folded into a good conversation turn (the garbled-transcript corruption bug), add garbled/held-out eval coverage, turn the AI eval into a quality bar, and ship it as a tracked release version.

**Architecture:** A new pure `AsrConfidenceGate` (Application layer) consults the per-segment Whisper confidence — a signal the text-only boundary classifier never sees — and suppresses a fold-into-an-existing-turn when confidence is below a tunable floor. Wired into `TranscriptPipelineService` just before routing. Deterministic and CI-tested. The classifier and its JSON contract are unchanged; eval corpora grow and the opt-in Haiku eval gains accuracy assertions.

**Tech Stack:** .NET 10, C# latest, xUnit + FluentAssertions + NSubstitute, System.Text.Json, FleetView source-gen Mediator. No new packages.

---

## Background for the implementer (read once)

- The boundary router lives in `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`, method `BuildCommandWithBoundaryAsync` (lines ~140–279). It computes a `result` (`BoundaryClassificationResult`), an optional `SplitDecision? guard`, then calls `RouteLabel(...)` which mutates the session and may return a `GenerateAnswerCommand`.
- `TranscriptItem.Confidence` (a `float`, 0..1) is the Whisper segment probability. It is **currently never read** in routing.
- "Fold-into-an-existing-turn" routes (the corruption surface) are the labels `QuestionContinued`, `AdditionalRequirement`, interviewer `ClarificationOfCurrentQuestion`, and a `NewQuestion` that `BoundarySplitGuard` demoted to `SplitDecision.AppendToActiveTurn`. All of these append text to the live turn and/or regenerate it.
- `Speaker.Me` utterances are routed out at the top of `BuildCommandWithBoundaryAsync` and never reach the gate point, so at the gate the speaker is always `Other`.
- Pattern references: `BoundarySplitGuard` (`src/AIHelperNET.Application/Sessions/BoundarySplitGuard.cs`) is the sibling pure-guard pattern to copy. `SplitConfidence.IsContinuationFamily` shows the label-set predicate style.
- Build/test gotcha: **stop the running overlay app before building** — it locks output DLLs (MSB3027). Run `dotnet test tests/AIHelperNET.Application.Tests` for the fast inner loop.

## File structure (what each task touches)

| File | Create/Modify | Responsibility |
|------|---------------|----------------|
| `src/AIHelperNET.Application/Sessions/AsrConfidenceGate.cs` | Create | Pure decision: drop a low-confidence fold? |
| `tests/AIHelperNET.Application.Tests/Sessions/AsrConfidenceGateTests.cs` | Create | Unit-test the gate's truth table |
| `src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs` | Modify | Add `AsrConfidence` field to `BoundaryDecisionRecord` |
| `tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs` | Modify | Update `Sample()` constructor for the new field |
| `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` | Modify | Hold a gate, call it before `RouteLabel`, record `AsrDropped` + `AsrConfidence` |
| `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` | Modify | Pipeline drop + companion-fold tests |
| `tests/AIHelperNET.Integration.Tests/Eval/boundary-garbled.json` | Create | ~7 garbled ASR cases |
| `tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json` | Modify | Grow 12 → ~24 entries |
| `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs` | Modify | Garbled eval test + accuracy assertions |
| `Directory.Build.props` | Modify | Introduce `<Version>` |
| `docs/superpowers/specs/2026-06-12-asr-degradation-hardening-design.md` | Modify | Fill in Results section |

---

## Task 1: `AsrConfidenceGate` pure helper

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/AsrConfidenceGate.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/AsrConfidenceGateTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Application.Tests/Sessions/AsrConfidenceGateTests.cs`:

```csharp
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class AsrConfidenceGateTests
{
    private readonly AsrConfidenceGate _gate = new();

    [Theory]
    [InlineData(BoundaryLabel.QuestionContinued)]
    [InlineData(BoundaryLabel.AdditionalRequirement)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion)]
    public void Drops_LowConfidenceFoldLabel_WithLiveTurn(BoundaryLabel fold)
    {
        _gate.ShouldDrop(asrConfidence: 0.30, foldLabel: fold, liveTurnExists: true)
            .Should().BeTrue();
    }

    [Fact]
    public void DoesNotDrop_WhenConfidenceAtOrAboveFloor()
    {
        _gate.ShouldDrop(asrConfidence: AsrConfidenceGate.AsrFloor,
            foldLabel: BoundaryLabel.QuestionContinued, liveTurnExists: true)
            .Should().BeFalse();
    }

    [Fact]
    public void DoesNotDrop_WhenNoLiveTurn()
    {
        _gate.ShouldDrop(asrConfidence: 0.10,
            foldLabel: BoundaryLabel.QuestionContinued, liveTurnExists: false)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(BoundaryLabel.NewQuestion)]
    [InlineData(BoundaryLabel.QuestionComplete)]
    [InlineData(BoundaryLabel.TaskComplete)]
    [InlineData(BoundaryLabel.QuestionStarted)]
    [InlineData(BoundaryLabel.Unrelated)]
    [InlineData(BoundaryLabel.NoQuestion)]
    public void DoesNotDrop_NonFoldLabel_EvenWhenLowConfidence(BoundaryLabel label)
    {
        _gate.ShouldDrop(asrConfidence: 0.10, foldLabel: label, liveTurnExists: true)
            .Should().BeFalse();
    }

    [Fact]
    public void IsFoldLabel_RecognizesTheThreeFoldLabels()
    {
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.QuestionContinued).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.AdditionalRequirement).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.ClarificationOfCurrentQuestion).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.NewQuestion).Should().BeFalse();
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail to compile**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AsrConfidenceGateTests"`
Expected: FAIL — `AsrConfidenceGate` does not exist (compile error).

- [ ] **Step 3: Write the implementation**

Create `src/AIHelperNET.Application/Sessions/AsrConfidenceGate.cs`:

```csharp
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Pure guard that suppresses a <em>fold-into-an-existing-turn</em> when the latest transcript
/// item's ASR (speech-recognition) confidence is too low to trust. Garbled audio that Whisper
/// transcribes into plausible-but-wrong words can be confidently mis-labelled as a continuation,
/// silently corrupting a good turn (see the 2026-06-11 field example). Whisper is often
/// confidently wrong on the <em>words</em>, so the only independent signal is the segment
/// probability — which the text-only boundary classifier never sees. This gate reads it.
/// No I/O, no clock — deterministically unit-testable.
/// </summary>
public sealed class AsrConfidenceGate
{
    /// <summary>
    /// Segment-confidence floor below which a fold is suppressed. Conservative starting value;
    /// tune from the <c>AsrConfidence</c> field now recorded on every boundary decision
    /// (<c>boundary-decisions-*.jsonl</c>).
    /// </summary>
    public const double AsrFloor = 0.45;

    /// <summary>
    /// Decides whether a fold-into-the-live-turn should be dropped as untrusted noise.
    /// </summary>
    /// <param name="asrConfidence">The latest item's Whisper segment probability (0..1).</param>
    /// <param name="foldLabel">The label that would drive routing. For a <c>NewQuestion</c> the
    /// split guard demoted to an append, pass <see cref="BoundaryLabel.QuestionContinued"/>.</param>
    /// <param name="liveTurnExists">Whether a non-terminal active turn exists to protect.</param>
    /// <returns><see langword="true"/> to drop (suppress the fold); otherwise route unchanged.</returns>
#pragma warning disable CA1822 // instance method intentional — callers hold an AsrConfidenceGate reference
    public bool ShouldDrop(double asrConfidence, BoundaryLabel foldLabel, bool liveTurnExists)
#pragma warning restore CA1822
    {
        if (!liveTurnExists)
            return false;
        if (asrConfidence >= AsrFloor)
            return false;
        return IsFoldLabel(foldLabel);
    }

    /// <summary>Fold labels: routes that append the item into the live turn (corruption surface).</summary>
    /// <param name="label">The label to test.</param>
    /// <returns><see langword="true"/> if the label folds into an existing turn.</returns>
    public static bool IsFoldLabel(BoundaryLabel label) =>
        label is BoundaryLabel.QuestionContinued
              or BoundaryLabel.AdditionalRequirement
              or BoundaryLabel.ClarificationOfCurrentQuestion;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AsrConfidenceGateTests"`
Expected: PASS (all theory cases green).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/AsrConfidenceGate.cs tests/AIHelperNET.Application.Tests/Sessions/AsrConfidenceGateTests.cs
git commit -m "feat(boundary): add AsrConfidenceGate pure helper"
```

---

## Task 2: Add `AsrConfidence` to the boundary decision record

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs`
- Modify: `tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs`

- [ ] **Step 1: Update the failing test first (recorder asserts the new field)**

In `JsonlBoundaryDecisionRecorderTests.cs`, add `AsrConfidence: 0.95,` to `Sample()` immediately after the `EffectiveConfidence: 0.40,` line:

```csharp
        EffectiveConfidence: 0.40,
        AsrConfidence: 0.95,
        Route: "AppendToActiveTurn",
```

Then extend `Record_WritesOneJsonLinePerCall` to assert the field serializes — add after the existing `Route` assertion (line 47):

```csharp
        doc.RootElement.GetProperty("AsrConfidence").GetDouble().Should().Be(0.95);
```

- [ ] **Step 2: Run the test to verify it fails to compile**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~JsonlBoundaryDecisionRecorderTests"`
Expected: FAIL — `BoundaryDecisionRecord` has no `AsrConfidence` parameter (compile error).

- [ ] **Step 3: Add the field to the record**

In `IBoundaryDecisionRecorder.cs`, add the positional parameter to `BoundaryDecisionRecord` immediately after `double EffectiveConfidence,`:

```csharp
    bool Agreed,
    double EffectiveConfidence,
    double AsrConfidence,
    string Route,
```

And add its `<param>` doc line inside the record's XML summary block (docs-as-errors requires it). Place it in the `<summary>`-adjacent param list — append this line just above the `public sealed record` declaration's existing doc, i.e. add to the doc comment:

```csharp
/// <param name="AsrConfidence">The latest item's Whisper segment probability (0..1) — the signal the ASR-confidence fold-guard uses.</param>
```

> Note: positional records document parameters via `<param>` tags on the type. If the existing record has no per-param tags (it documents via the `<summary>` only), instead add the `<param>` tags for **all** parameters is NOT required — only the analyzer-flagged new one. If the build complains about missing `<param>` tags on the *other* params, that means the record uses `<param>` tags throughout; in that case add tags for every parameter. Run the build (Step 4) to see which applies and follow the compiler.

- [ ] **Step 4: Build to verify the record compiles and find any other call sites**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj`
Expected: This will FAIL at the one other construction site — `TranscriptPipelineService.cs` (the `recorder?.Record(new BoundaryDecisionRecord(...))` call). That is fixed in Task 3. For now confirm the **only** errors are (a) docs/param if any, and (b) the pipeline call site. Fix any param-doc error here; leave the pipeline error for Task 3.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/IBoundaryDecisionRecorder.cs tests/AIHelperNET.Infrastructure.Tests/Diagnostics/JsonlBoundaryDecisionRecorderTests.cs
git commit -m "feat(boundary): record AsrConfidence on each boundary decision"
```

> The solution does not fully build until Task 3 wires the pipeline call site. That is expected and fine for a WIP commit on a feature branch; the next task closes it.

---

## Task 3: Wire the gate into the pipeline

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

- [ ] **Step 1: Write the failing pipeline tests**

Add these two tests to `TranscriptPipelineServiceTests.cs` (inside the boundary-path region, after the existing boundary tests). They use the existing `MakeSvcWithBoundary`, `MakeSession`, and `MakeItem` helpers. `MakeItem`'s confidence is fixed at `0.9f`, so add a local low-confidence item helper inside each test via `TranscriptItem.Create`.

```csharp
    [Fact]
    public async Task BoundaryPath_LowAsrConfidenceContinuation_IsDropped_NotFolded()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        // Live, already-answered turn that a fold would corrupt.
        var q = DetectedQuestion.Create("Explain write amplification.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain write amplification.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);
        var originalQuestion = turn.QuestionText;

        // Heuristic will be low-confidence on this garbled text → AI is consulted.
        // AI substitute returns QuestionContinued at 0.72 (mirrors the field example).
        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.72,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "welcome through how you would add cash in",
                Reason: "test")));

        var (svc, mediator, _, uow, _, recorder) = MakeSvcWithBoundary(transcriptSink, classifier);

        // Garbled interviewer line with LOW Whisper confidence (below AsrFloor 0.45).
        var garbled = TranscriptItem.Create(
            Speaker.Other, "welcome through how you would add cash in without service day of data",
            T0.AddSeconds(1), 0.30f);
        await svc.ProcessAsync(session, garbled, uow, CancellationToken.None);

        // Turn unchanged — the garbled fragment was NOT appended.
        turn.QuestionText.Should().Be(originalQuestion);
        session.ConversationTurns.Should().HaveCount(1);

        // No regeneration command fired.
        await Task.Delay(150);
        await mediator.DidNotReceive().Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());

        // Decision recorded with the AsrDropped route.
        recorder.Received().Record(Arg.Is<BoundaryDecisionRecord>(r => r.Route == "AsrDropped"));
    }

    [Fact]
    public async Task BoundaryPath_HighAsrConfidenceContinuation_StillFolds()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var q = DetectedQuestion.Create("Explain write amplification.", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Explain write amplification.", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var classifier = Substitute.For<IQuestionBoundaryClassifier>();
        classifier
            .ClassifyAsync(Arg.Any<ConversationTurnStatus?>(), Arg.Any<IReadOnlyList<TranscriptItem>>(),
                Arg.Any<TranscriptItem>(), Arg.Any<Speaker>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BoundaryClassificationResult(
                BoundaryLabel.QuestionContinued, 0.72,
                ShouldGenerateAnswer: false, ShouldRefineExistingAnswer: true,
                ShouldCreateNewTurn: false,
                NormalizedQuestionText: "and how does it affect SSD lifespan",
                Reason: "test")));

        var (svc, _, _, uow, _, recorder) = MakeSvcWithBoundary(transcriptSink, classifier);

        // CLEAN interviewer continuation with HIGH Whisper confidence (above AsrFloor).
        var clean = TranscriptItem.Create(
            Speaker.Other, "and how does it affect SSD lifespan over time", T0.AddSeconds(1), 0.90f);
        await svc.ProcessAsync(session, clean, uow, CancellationToken.None);

        // Fold happened — fragment appended to the live turn's question.
        turn.QuestionText.Should().Contain("SSD lifespan");
        recorder.DidNotReceive().Record(Arg.Is<BoundaryDecisionRecord>(r => r.Route == "AsrDropped"));
    }
```

> If `ConversationTurn` exposes the question text under a different member than `QuestionText`, adjust both assertions to that member (check `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs`). The behavior asserted (unchanged vs appended) is what matters.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~LowAsrConfidenceContinuation|FullyQualifiedName~HighAsrConfidenceContinuation"`
Expected: FAIL — the low-confidence case currently folds (turn text changes, no `AsrDropped` record). (Also the project won't compile until the Task-2 pipeline call site is fixed below; that is part of this task.)

- [ ] **Step 3a: Add the gate field**

In `TranscriptPipelineService.cs`, next to the other readonly guards (after `private readonly BoundarySplitGuard _splitGuard = new();`, line ~33), add:

```csharp
    private readonly AsrConfidenceGate _asrGate = new();
```

- [ ] **Step 3b: Compute the drop decision and gate the route**

In `BuildCommandWithBoundaryAsync`, replace the routing line and the record construction. Find this block (lines ~244 and ~260–276):

```csharp
        var cmd = RouteLabel(session, item, result, activeTurn, guard);
```

Replace it with:

```csharp
        // ASR-confidence fold-guard: a low-confidence (garbled) item that would fold into the live
        // turn is dropped as untrusted noise rather than silently corrupting that turn. A
        // guard-demoted NewQuestion folds via append, so it is gated as a continuation.
        var foldCandidate =
            result.Classification == BoundaryLabel.NewQuestion && guard == SplitDecision.AppendToActiveTurn
                ? BoundaryLabel.QuestionContinued
                : result.Classification;
        var liveTurn = activeTurn is not null && !IsTerminal(activeTurn.Status);
        var asrDropped = _asrGate.ShouldDrop(item.Confidence, foldCandidate, liveTurn);

        var cmd = asrDropped ? null : RouteLabel(session, item, result, activeTurn, guard);
```

Then in the `recorder?.Record(new BoundaryDecisionRecord(...))` call, set the `Route` and add the `AsrConfidence` field. Change the `Route:` line and insert `AsrConfidence:` after `EffectiveConfidence:`:

```csharp
            Agreed: agreed,
            EffectiveConfidence: effectiveConfidence,
            AsrConfidence: item.Confidence,
            Route: asrDropped ? "AsrDropped" : (guard?.ToString() ?? result.Classification.ToString()),
            FinalLabel: result.Classification,
```

- [ ] **Step 3c: Add `AsrConfidence` to the structured log line**

In the `Log.BoundaryRouted(...)` call (line ~253) and its `[LoggerMessage]` definition (line ~748), add the ASR confidence. Update the call to pass `item.Confidence` as a new final argument before `result.Classification`/text — simplest is to append it. Change the call:

```csharp
            Log.BoundaryRouted(logger, item.Speaker, activeTurnStatus,
                heuristic.Classification, heuristic.Confidence, aiLabel, aiConfidence,
                agreed, effectiveConfidence, guardStr, item.Confidence,
                result.Classification, result.Confidence,
                item.Text[..Math.Min(60, item.Text.Length)]);
```

And the definition:

```csharp
        [LoggerMessage(Level = LogLevel.Information,
            Message = "BoundaryRoute: speaker={Speaker} staleStatus={Status} " +
                      "heuristic={Heuristic}({HeuristicConf:F2}) ai={AiLabel}({AiConf}) " +
                      "agreed={Agreed} eff={Effective:F2} guard={Guard} asr={Asr:F2} -> {Label} ({Confidence:F2}) text='{Text}'")]
        internal static partial void BoundaryRouted(
            ILogger logger, Speaker speaker, ConversationTurnStatus? status,
            BoundaryLabel heuristic, double heuristicConf, BoundaryLabel? aiLabel, double? aiConf,
            bool agreed, double effective, string guard, double asr,
            BoundaryLabel label, double confidence, string text);
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Application.Tests`
Expected: PASS — both new tests green, and all pre-existing pipeline tests still green (no regression).

- [ ] **Step 5: Full solution build + test**

Run: `dotnet build` then `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~JsonlBoundaryDecisionRecorderTests"`
Expected: PASS — the Task-2 recorder test now passes (call site fixed), solution compiles.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat(boundary): drop low-ASR-confidence folds, record AsrConfidence + AsrDropped"
```

---

## Task 4: Garbled corpus + opt-in eval test

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/boundary-garbled.json`
- Modify: `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs`
- Modify: `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj` (only if JSON files are not auto-copied — verify first)

- [ ] **Step 1: Verify the corpus JSON copy mechanism**

Run: `dotnet build tests/AIHelperNET.Integration.Tests` then check the output dir for the existing corpus:
Run: `Test-Path tests/AIHelperNET.Integration.Tests/bin/Debug/net10.0-windows10.0.17763.0/Eval/boundary-holdout.json`
Expected: `True` (existing `Eval/*.json` already copy to output via the csproj — confirm there is a `<None ... CopyToOutputDirectory>` or wildcard `Content` glob covering `Eval/*.json`). If `boundary-garbled.json` will be covered by the same glob, no csproj edit is needed. If the csproj lists files individually, add `boundary-garbled.json` mirroring the `boundary-holdout.json` entry.

- [ ] **Step 2: Create the garbled corpus**

Create `tests/AIHelperNET.Integration.Tests/Eval/boundary-garbled.json`. Each case is a realistic Whisper mistranscription; the property under test is that none is confidently labelled a fold label. Expected labels are `Unrelated`/`NoQuestion` (garbled text is not a trustworthy continuation):

```json
[
  { "id": "g-caching-field", "recentItems": [ { "speaker": "Other", "text": "we are seeing a spike in writes" } ], "latestItem": { "speaker": "Other", "text": "welcome through how you would add cash in without service day of data" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "real field mangling of 'walk me through how you'd add caching without serving stale data' — must NOT be QuestionContinued/AdditionalRequirement" },
  { "id": "g-process-thread", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what's the differents between a prophet and a thread pull" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "homophone garble of process/thread question" },
  { "id": "g-index-tradeoff", "recentItems": [ { "speaker": "Other", "text": "design a key value store" } ], "latestItem": { "speaker": "Other", "text": "and what about the in the next trade of for right heavy" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "dropped/garbled words ('index tradeoffs for write-heavy') — not a confident continuation" },
  { "id": "g-oauth-noise", "recentItems": [ { "speaker": "Other", "text": "explain how oauth works" } ], "latestItem": { "speaker": "Other", "text": "oh off too scoped come in to play the where" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "garbled 'where do scopes come into play' — word-salad, not AdditionalRequirement" },
  { "id": "g-blank-ish", "recentItems": [], "latestItem": { "speaker": "Other", "text": "uh the the so kind of" }, "activeTurnStatus": null, "expectedLabel": "NoQuestion", "note": "near-empty disfluent garble — no content" },
  { "id": "g-rate-limit", "recentItems": [ { "speaker": "Other", "text": "design an api gateway" } ], "latestItem": { "speaker": "Other", "text": "how do you red limit the bucket leak in token" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "Unrelated", "note": "garbled rate-limiting/leaky-bucket — must not fold as continuation" },
  { "id": "g-schema-garble", "recentItems": [], "latestItem": { "speaker": "Other", "text": "sketch out the scheme are for a movie booking sister" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "garble of 'schema for a movie booking system' — degraded imperative" }
]
```

- [ ] **Step 3: Add the opt-in garbled eval test**

In `BoundaryClassifierAiEvalTests.cs`, add a third test mirroring `RealHaiku_OverHoldout_ProducesReport`:

```csharp
    [Fact]
    public async Task RealHaiku_OverGarbled_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the garbled eval.");
            return;
        }
        await RunEvalAsync(apiKey, CorpusLoader.Load("boundary-garbled.json"),
            "Real Haiku — GARBLED (ASR degradation)", "ai-eval-garbled", minAccuracy: 0.80,
            forbidFoldLabels: true);
    }
```

> `RunEvalAsync` gains two parameters in Task 6; this test passes them now. If implementing Task 6 after this, temporarily call the 4-arg overload and add the assertion params in Task 6. Recommended: do Task 6 Step 3 (signature change) together with this step so the project compiles.

- [ ] **Step 4: Build to verify the corpus loads**

Run: `dotnet build tests/AIHelperNET.Integration.Tests`
Expected: PASS (compiles; `boundary-garbled.json` copies to output). The new test is opt-in and will be exercised in Task 6/8.

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/boundary-garbled.json tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj
git commit -m "test(eval): add garbled ASR boundary corpus + opt-in eval"
```

---

## Task 5: Expand the held-out corpus 12 → ~24

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json`

- [ ] **Step 1: Append 12 novel held-out entries**

Insert these entries into the existing `boundary-holdout.json` array (before the closing `]`, after the last existing entry — add a comma after the current last entry `uh-ack-closure`). Topics/phrasings are absent from both the tuning corpus and the prompt examples:

```json
  ,
  { "id": "qh2-cap-theorem", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what does the cap theorem actually force you to give up" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "held-out: direct question, distributed-systems topic" },
  { "id": "qh2-gc-pauses", "recentItems": [], "latestItem": { "speaker": "Other", "text": "how would you diagnose long gc pauses in production" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "held-out: ops/runtime topic" },
  { "id": "th2-lru-cache", "recentItems": [], "latestItem": { "speaker": "Other", "text": "implement an lru cache with o of one get and put" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "held-out: imperative coding task" },
  { "id": "th2-rate-limiter", "recentItems": [], "latestItem": { "speaker": "Other", "text": "design a distributed rate limiter for our public api" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "held-out: imperative design task" },
  { "id": "sh2-metrics-pipeline", "recentItems": [], "latestItem": { "speaker": "Other", "text": "imagine we collect billions of metric points a day" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "held-out: scenario setup, novel 'imagine we'" },
  { "id": "ch2-sharding-keys", "recentItems": [ { "speaker": "Other", "text": "design a url shortener" } ], "latestItem": { "speaker": "Other", "text": "so how do you pick the shard key for that" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "held-out over-split: 'so how do you' continuation, same scenario" },
  { "id": "ch2-retry-collecting", "recentItems": [ { "speaker": "Other", "text": "build a job queue" } ], "latestItem": { "speaker": "Other", "text": "and it should retry failed jobs with backoff" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "QuestionContinued", "note": "held-out status-gated: 'and it should' while collecting = continued" },
  { "id": "ah2-multiregion", "recentItems": [ { "speaker": "Other", "text": "design a session store" } ], "latestItem": { "speaker": "Other", "text": "also it has to work across multiple regions" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "held-out: new constraint on an answered turn" },
  { "id": "clh2-scope-input", "recentItems": [ { "speaker": "Other", "text": "parse this log format" } ], "latestItem": { "speaker": "Me", "text": "should i assume the input always fits in memory" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "held-out: candidate Me clarifies constraints" },
  { "id": "nh2-switch-coding", "recentItems": [ { "speaker": "Other", "text": "explain database indexing" } ], "latestItem": { "speaker": "Other", "text": "ok lets switch gears and do a coding problem instead" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "held-out: explicit 'switch gears' topic marker" },
  { "id": "uh2-water", "recentItems": [ { "speaker": "Other", "text": "what is eventual consistency" } ], "latestItem": { "speaker": "Other", "text": "give me one sec grabbing some water" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "held-out: logistics filler" },
  { "id": "uh2-praise", "recentItems": [ { "speaker": "Other", "text": "how does tcp handle congestion" } ], "latestItem": { "speaker": "Other", "text": "perfect that is exactly what i was looking for" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "held-out: interviewer acknowledgement" }
```

- [ ] **Step 2: Validate the JSON parses**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~CorpusLoader" -l "console;verbosity=minimal"`
Expected: PASS if a loader test exists; otherwise run a quick parse check:
Run: `pwsh -c "Get-Content tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json -Raw | ConvertFrom-Json | Measure-Object | Select-Object Count"`
Expected: `Count : 24`.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/boundary-holdout.json
git commit -m "test(eval): grow held-out boundary corpus 12 -> 24"
```

---

## Task 6: Eval-as-guard — accuracy assertions when a key is present

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs`

- [ ] **Step 1: Change `RunEvalAsync` to accept and assert thresholds**

Replace the `RunEvalAsync` signature and the tail of its body. New signature adds `minAccuracy` and `forbidFoldLabels`:

```csharp
    private async Task RunEvalAsync(
        string apiKey, IReadOnlyList<CorpusEntry> corpus, string title, string reportPrefix,
        double minAccuracy = 0.0, bool forbidFoldLabels = false)
```

At the end of the method, after `output.WriteLine($"Report written to {reportPath}");`, add the assertions:

```csharp
        if (minAccuracy > 0.0)
            matrix.Accuracy.Should().BeGreaterThanOrEqualTo(minAccuracy,
                $"{title}: Haiku accuracy regressed below the guarded floor");

        if (forbidFoldLabels)
        {
            // Garbled input must never be confidently routed as a fold (QuestionContinued /
            // AdditionalRequirement) — that is the text-side of the corruption bug.
            var foldMisses = misses.Where(m =>
                m.Contains("got QuestionContinued", StringComparison.Ordinal) ||
                m.Contains("got AdditionalRequirement", StringComparison.Ordinal)).ToList();
            foldMisses.Should().BeEmpty(
                $"{title}: garbled items were classified as fold labels: {string.Join("; ", foldMisses)}");
        }
```

Add `using FluentAssertions;` to the file's usings if not present.

- [ ] **Step 2: Add the floor to the held-out test**

Update `RealHaiku_OverHoldout_ProducesReport`'s `RunEvalAsync` call to pass the floor:

```csharp
        await RunEvalAsync(apiKey, CorpusLoader.Load("boundary-holdout.json"),
            "Real Haiku — HELD-OUT (generalization)", "ai-eval-holdout", minAccuracy: 0.90);
```

Leave `RealHaiku_OverCorpus_ProducesReport` without a floor (the tuning corpus is the development input, not a guard).

- [ ] **Step 3: Build**

Run: `dotnet build tests/AIHelperNET.Integration.Tests`
Expected: PASS. The eval tests remain opt-in (skip with no key); keyless CI is unchanged.

- [ ] **Step 4: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs
git commit -m "test(eval): assert Haiku accuracy floor + no fold-label on garbled when key present"
```

---

## Task 7: Introduce the release version

**Files:**
- Modify: `Directory.Build.props`

- [ ] **Step 1: Add version properties**

In `Directory.Build.props`, add inside the existing `<PropertyGroup>` (after `<GenerateDocumentationFile>true</GenerateDocumentationFile>`):

```xml
    <Version>0.1.0</Version>
    <AssemblyVersion>0.1.0.0</AssemblyVersion>
    <FileVersion>0.1.0.0</FileVersion>
```

> Version number is the one user-confirmed open decision. `0.1.0` = first tracked version of a pre-1.0 app. If the user picked a different number, use that instead.

- [ ] **Step 2: Verify the build stamps it**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: PASS. Optionally confirm:
Run: `pwsh -c "(Get-Item src/AIHelperNET.App/bin/Debug/net10.0-windows/AIHelperNET.App.dll).VersionInfo.FileVersion"`
Expected: `0.1.0.0`.

- [ ] **Step 3: Commit**

```bash
git add Directory.Build.props
git commit -m "chore(release): introduce tracked version 0.1.0"
```

---

## Task 8: Full verification + record Haiku results

**Files:**
- Modify: `docs/superpowers/specs/2026-06-12-asr-degradation-hardening-design.md` (Results section)

- [ ] **Step 1: Full deterministic suite (no key)**

Run: `dotnet test`
Expected: PASS across Domain / Application / Infrastructure / Integration (the known SQLite-lock integration flake and the 2 parked UITest flakes are pre-existing — confirm no *new* failures). The new `AsrConfidenceGateTests`, pipeline drop/fold tests, and recorder test are green.

- [ ] **Step 2: Run the opt-in Haiku eval (requires an API key)**

This is a human-run step — it costs money and hits the network. In **bash** (not PowerShell — inline env var):

```bash
AIHELPER_AI_EVAL_KEY="<anthropic-key>" dotnet test tests/AIHelperNET.Integration.Tests \
  --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests" -l "console;verbosity=detailed"
```

Expected: held-out ≥ 0.90 (asserted), garbled ≥ 0.80 with no fold-label misses (asserted), corpus report produced. Reports land in `AppPaths.DiagnosticsDir` (`ai-eval-holdout-*.txt`, `ai-eval-garbled-*.txt`).

- [ ] **Step 3: Record the numbers in the spec**

Fill in the spec's `## Results` section with: the shipped `AsrFloor` (0.45 unless tuned), the held-out accuracy, the garbled accuracy + that no garbled item was labelled a fold, and the released version (`0.1.0`). Commit:

```bash
git add docs/superpowers/specs/2026-06-12-asr-degradation-hardening-design.md
git commit -m "docs(spec): record ASR-hardening Haiku eval results"
```

---

## Task 9: PR to develop + cut the release

- [ ] **Step 1: Push and open the PR**

```bash
git push -u origin feature/asr-degradation-hardening
gh pr create --base develop --title "ASR-degradation hardening + eval-as-guard (v0.1.0)" --body "<summary of the gate, corpora, eval guards, and version>"
```

> Repo convention: push ALL commits before `gh pr create`. The user merges PRs (auto-merge is not guaranteed) — do not assume merge on creation.

- [ ] **Step 2: After merge to develop, cut the release (gitflow)**

This is a user-gated action — confirm the version number first. Then:

```bash
git checkout develop && git pull
git checkout -b release/0.1.0
git push -u origin release/0.1.0
git tag v0.1.0 && git push origin v0.1.0
```

> Per gitflow `release/*` flows to `master`. Do NOT merge to master without the user's explicit go-ahead.

---

## Self-review notes

- **Spec coverage:** Component A → Tasks 1+3; observability field → Task 2; Component B garbled → Task 4, held-out → Task 5; Component C assertions → Task 6, deterministic CI guards → Tasks 1+3; Component D version → Task 7; results/release → Tasks 8+9. All spec sections map to a task.
- **Type consistency:** `AsrConfidenceGate.ShouldDrop(double, BoundaryLabel, bool)` and `AsrConfidenceGate.AsrFloor` / `IsFoldLabel` are used identically in Tasks 1 and 3. `BoundaryDecisionRecord.AsrConfidence` (double) defined in Task 2, set in Task 3, asserted in Task 2's test. `RunEvalAsync(..., double minAccuracy, bool forbidFoldLabels)` defined in Task 6, called in Tasks 4 and 6.
- **Open verification the executor must do live:** the exact `ConversationTurn` question-text member name (Task 3 note), and the corpus-JSON copy glob in the Integration csproj (Task 4 Step 1).
