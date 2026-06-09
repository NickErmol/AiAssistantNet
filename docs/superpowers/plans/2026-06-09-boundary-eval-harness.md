# Boundary-classifier Eval Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic eval apparatus that scores boundary-classification accuracy against a hand-authored labeled corpus, with a CI heuristic-regression guard and an opt-in real-Haiku tuning harness — no tuning performed.

**Architecture:** A pure `ConfusionMatrix`/`EvalReport` core and a JSON corpus model live in the Integration.Tests project. A deterministic xUnit test drives the pure `QuestionBoundaryDetector` over the corpus and asserts accuracy ≥ a measured baseline. A second, env-gated test drives the real `QuestionBoundaryClassifier` (Haiku) over the same corpus and writes a report; it returns early (passing) when the key env var is absent, so CI never calls the network.

**Tech Stack:** .NET 10, C#, xUnit 2.9.3 + FluentAssertions 8.10.0, System.Text.Json, the existing `QuestionBoundaryDetector` (Domain) and `QuestionBoundaryClassifier` (Infrastructure).

---

## File Structure

**Create:**
- `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrix.cs` — pure tally of expected×predicted with accuracy/precision/recall.
- `tests/AIHelperNET.Integration.Tests/Eval/EvalReport.cs` — renders a `ConfusionMatrix` to text.
- `tests/AIHelperNET.Integration.Tests/Eval/CorpusEntry.cs` — corpus DTO + loader.
- `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json` — the labeled corpus.
- `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrixTests.cs`
- `tests/AIHelperNET.Integration.Tests/Eval/CorpusLoaderTests.cs`
- `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs`
- `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs`

**Modify:**
- `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj` — copy the corpus JSON to output.

---

## Task 1: Pure `ConfusionMatrix` + `EvalReport`

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrix.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Eval/EvalReport.cs`
- Test: `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrixTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrixTests.cs`:

```csharp
using AIHelperNET.Domain.Questions;
using AIHelperNET.Integration.Tests.Eval;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class ConfusionMatrixTests
{
    [Fact]
    public void Accuracy_CountsCorrectOverTotal()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion);   // correct
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued); // wrong
        m.Record(BoundaryLabel.Unrelated, BoundaryLabel.Unrelated);       // correct

        m.Total.Should().Be(3);
        m.Accuracy.Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void EmptyMatrix_AccuracyIsZero()
    {
        new ConfusionMatrix().Accuracy.Should().Be(0.0);
    }

    [Fact]
    public void Count_ReturnsCellTally()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued);
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued);

        m.Count(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued).Should().Be(2);
        m.Count(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion).Should().Be(0);
    }

    [Fact]
    public void PrecisionAndRecall_ComputedPerLabel()
    {
        var m = new ConfusionMatrix();
        // NewQuestion: 2 expected, 1 predicted correctly; one other item also predicted NewQuestion.
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion);       // TP
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued); // FN for NewQuestion
        m.Record(BoundaryLabel.Unrelated,   BoundaryLabel.NewQuestion);       // FP for NewQuestion

        // Precision = TP / (TP+FP) = 1 / 2
        m.PrecisionFor(BoundaryLabel.NewQuestion).Should().BeApproximately(0.5, 1e-9);
        // Recall = TP / (TP+FN) = 1 / 2
        m.RecallFor(BoundaryLabel.NewQuestion).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void PrecisionRecall_ZeroWhenLabelAbsent()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.Unrelated, BoundaryLabel.Unrelated);

        m.PrecisionFor(BoundaryLabel.NewQuestion).Should().Be(0.0);
        m.RecallFor(BoundaryLabel.NewQuestion).Should().Be(0.0);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~ConfusionMatrixTests"`
Expected: FAIL — `ConfusionMatrix` does not exist (compile error).

- [ ] **Step 3: Write `ConfusionMatrix`**

Create `tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrix.cs`:

```csharp
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Pure expected×predicted tally for boundary-label classification, with accuracy/precision/recall.</summary>
public sealed class ConfusionMatrix
{
    private readonly Dictionary<(BoundaryLabel Expected, BoundaryLabel Predicted), int> _cells = new();
    private int _total;
    private int _correct;

    /// <summary>Records one (expected, predicted) outcome.</summary>
    public void Record(BoundaryLabel expected, BoundaryLabel predicted)
    {
        var key = (expected, predicted);
        _cells[key] = _cells.TryGetValue(key, out var n) ? n + 1 : 1;
        _total++;
        if (expected == predicted) _correct++;
    }

    /// <summary>Total recorded outcomes.</summary>
    public int Total => _total;

    /// <summary>Fraction correct, or 0 when nothing recorded.</summary>
    public double Accuracy => _total == 0 ? 0.0 : (double)_correct / _total;

    /// <summary>The tally for one cell.</summary>
    public int Count(BoundaryLabel expected, BoundaryLabel predicted) =>
        _cells.TryGetValue((expected, predicted), out var n) ? n : 0;

    /// <summary>Precision for a label: correct predictions of it / all predictions of it (0 if none).</summary>
    public double PrecisionFor(BoundaryLabel label)
    {
        var predicted = _cells.Where(kv => kv.Key.Predicted == label).Sum(kv => kv.Value);
        return predicted == 0 ? 0.0 : (double)Count(label, label) / predicted;
    }

    /// <summary>Recall for a label: correct predictions of it / all actual occurrences of it (0 if none).</summary>
    public double RecallFor(BoundaryLabel label)
    {
        var expected = _cells.Where(kv => kv.Key.Expected == label).Sum(kv => kv.Value);
        return expected == 0 ? 0.0 : (double)Count(label, label) / expected;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~ConfusionMatrixTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Write `EvalReport`**

Create `tests/AIHelperNET.Integration.Tests/Eval/EvalReport.cs`:

```csharp
using System.Globalization;
using System.Text;
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Renders a <see cref="ConfusionMatrix"/> to a human-readable text block.</summary>
public static class EvalReport
{
    private static readonly BoundaryLabel[] Labels = Enum.GetValues<BoundaryLabel>();

    /// <summary>Builds a report: overall accuracy, a per-label precision/recall table, and the confusion grid.</summary>
    public static string ToText(ConfusionMatrix matrix, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"=== {title} ===");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Total={matrix.Total} Accuracy={matrix.Accuracy:P1}");
        sb.AppendLine();

        sb.AppendLine("Per-label precision / recall:");
        foreach (var label in Labels)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {label,-32} P={matrix.PrecisionFor(label):P0}  R={matrix.RecallFor(label):P0}");
        sb.AppendLine();

        sb.AppendLine("Confusion (rows=expected, cols=predicted), non-zero cells only:");
        foreach (var exp in Labels)
            foreach (var pred in Labels)
            {
                var n = matrix.Count(exp, pred);
                if (n > 0)
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"  {exp} -> {pred}: {n}{(exp == pred ? "" : "   <-- miss")}");
            }
        return sb.ToString();
    }
}
```

- [ ] **Step 6: Build to verify `EvalReport` compiles**

Run: `dotnet build tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
Expected: Build succeeded.

- [ ] **Step 7: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrix.cs tests/AIHelperNET.Integration.Tests/Eval/EvalReport.cs tests/AIHelperNET.Integration.Tests/Eval/ConfusionMatrixTests.cs
git commit -m "test(eval): pure ConfusionMatrix + EvalReport for boundary eval"
```

---

## Task 2: Corpus model, loader, JSON file, and sanity test

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/CorpusEntry.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json`
- Modify: `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
- Test: `tests/AIHelperNET.Integration.Tests/Eval/CorpusLoaderTests.cs`

- [ ] **Step 1: Add the corpus DTO + loader**

Create `tests/AIHelperNET.Integration.Tests/Eval/CorpusEntry.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>One transcript item in a corpus entry's recent-context window.</summary>
public sealed record CorpusItem(Speaker Speaker, string Text);

/// <summary>A single labeled classification case: inputs + the human-assigned correct label.</summary>
public sealed record CorpusEntry(
    string Id,
    IReadOnlyList<CorpusItem> RecentItems,
    CorpusItem LatestItem,
    ConversationTurnStatus? ActiveTurnStatus,
    BoundaryLabel ExpectedLabel,
    string? Note);

/// <summary>Loads the checked-in boundary corpus from the test output directory.</summary>
public static class CorpusLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Path to the corpus JSON copied next to the test assembly.</summary>
    public static string CorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Eval", "boundary-corpus.json");

    /// <summary>Deserializes the corpus. Throws if the file is missing, malformed, or empty.</summary>
    public static IReadOnlyList<CorpusEntry> Load()
    {
        var json = File.ReadAllText(CorpusPath);
        var entries = JsonSerializer.Deserialize<List<CorpusEntry>>(json, Options)
            ?? throw new InvalidOperationException("Corpus deserialized to null.");
        if (entries.Count == 0)
            throw new InvalidOperationException("Corpus is empty.");
        return entries;
    }
}
```

- [ ] **Step 2: Create the corpus JSON**

Create `tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json` with exactly this content:

```json
[
  { "id": "filler-thanks", "recentItems": [], "latestItem": { "speaker": "Other", "text": "okay great thank you so much" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "social filler" },
  { "id": "filler-screenshare", "recentItems": [], "latestItem": { "speaker": "Other", "text": "give me a second to share my screen" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "logistics filler" },
  { "id": "filler-canyouhear", "recentItems": [], "latestItem": { "speaker": "Other", "text": "can you hear me okay now" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "audio-check filler" },
  { "id": "scenario-start-payment", "recentItems": [], "latestItem": { "speaker": "Other", "text": "let's say we have a payment service that processes orders" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup, not yet answerable" },
  { "id": "scenario-start-imagine", "recentItems": [], "latestItem": { "speaker": "Other", "text": "imagine that we are building a high traffic api" }, "activeTurnStatus": null, "expectedLabel": "QuestionStarted", "note": "scenario setup" },
  { "id": "qcomplete-di", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what is dependency injection in dotnet?" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "direct question" },
  { "id": "qcomplete-solid", "recentItems": [], "latestItem": { "speaker": "Other", "text": "how do the solid principles apply to microservices?" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "direct question" },
  { "id": "qcomplete-interrog-noqmark", "recentItems": [], "latestItem": { "speaker": "Other", "text": "what are the tradeoffs between sql and nosql databases" }, "activeTurnStatus": null, "expectedLabel": "QuestionComplete", "note": "interrogative start, no question mark" },
  { "id": "task-explain-gc", "recentItems": [], "latestItem": { "speaker": "Other", "text": "explain how garbage collection works in the clr" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative task" },
  { "id": "task-design-ratelimiter", "recentItems": [], "latestItem": { "speaker": "Other", "text": "design a distributed rate limiter for our gateway" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative task" },
  { "id": "task-implement-debounce", "recentItems": [], "latestItem": { "speaker": "Other", "text": "implement a debounce function in typescript" }, "activeTurnStatus": null, "expectedLabel": "TaskComplete", "note": "imperative task" },
  { "id": "continued-collecting-retries", "recentItems": [ { "speaker": "Other", "text": "let's say we have a payment service" } ], "latestItem": { "speaker": "Other", "text": "and it needs to handle retries on failure" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "QuestionContinued", "note": "continuation while collecting" },
  { "id": "continued-collecting-also", "recentItems": [ { "speaker": "Other", "text": "design a url shortener" } ], "latestItem": { "speaker": "Other", "text": "also it should support custom aliases" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "QuestionContinued", "note": "continuation while collecting" },
  { "id": "addreq-idempotent", "recentItems": [ { "speaker": "Other", "text": "design an order service" } ], "latestItem": { "speaker": "Other", "text": "also assume the service must be idempotent" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "new constraint on an answered turn" },
  { "id": "addreq-aswell", "recentItems": [ { "speaker": "Other", "text": "explain caching strategies" } ], "latestItem": { "speaker": "Other", "text": "also consider memory pressure as well" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "AdditionalRequirement", "note": "additional requirement" },
  { "id": "clarif-me-readwrite", "recentItems": [ { "speaker": "Other", "text": "how would you scale the database?" } ], "latestItem": { "speaker": "Me", "text": "do you mean for the read path or the write path?" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarifies while interviewer is collecting" },
  { "id": "clarif-me-scope", "recentItems": [ { "speaker": "Other", "text": "walk me through your testing approach" } ], "latestItem": { "speaker": "Me", "text": "should i focus on unit or integration testing?" }, "activeTurnStatus": "CollectingQuestion", "expectedLabel": "ClarificationOfCurrentQuestion", "note": "candidate clarification" },
  { "id": "newq-moveon-kubernetes", "recentItems": [ { "speaker": "Other", "text": "what is dependency injection?" } ], "latestItem": { "speaker": "Other", "text": "now let's move on to how you'd design a deployment pipeline?" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "explicit new-topic marker after an answered turn" },
  { "id": "newq-another-question", "recentItems": [ { "speaker": "Other", "text": "explain rest versus grpc" } ], "latestItem": { "speaker": "Other", "text": "another question, how do you handle schema migrations?" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "explicit new-topic marker" },
  { "id": "scenarioA-cache-invalidation", "recentItems": [ { "speaker": "Other", "text": "let's talk about caching strategies" } ], "latestItem": { "speaker": "Other", "text": "how would you handle cache invalidation here" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "Scenario A over-split: a continuation that reads like a new question" },
  { "id": "scenarioA-followup-detail", "recentItems": [ { "speaker": "Other", "text": "design a notification system" } ], "latestItem": { "speaker": "Other", "text": "and how would the retry logic work in that design" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "QuestionContinued", "note": "Scenario A: refinement of the same question" },
  { "id": "scenarioB-different-topic", "recentItems": [ { "speaker": "Other", "text": "explain how dependency injection works" } ], "latestItem": { "speaker": "Other", "text": "completely different topic, what's your experience with kubernetes?" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "NewQuestion", "note": "Scenario B under-split: genuine new question after an answered turn" },
  { "id": "noise-short", "recentItems": [], "latestItem": { "speaker": "Other", "text": "right exactly yeah" }, "activeTurnStatus": null, "expectedLabel": "Unrelated", "note": "too short / agreement noise" },
  { "id": "noise-statement", "recentItems": [ { "speaker": "Other", "text": "what is your favorite language?" } ], "latestItem": { "speaker": "Other", "text": "that makes a lot of sense thanks for sharing" }, "activeTurnStatus": "PreliminaryReady", "expectedLabel": "Unrelated", "note": "interviewer acknowledgement, not a question" }
]
```

- [ ] **Step 3: Copy the corpus to the test output directory**

In `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`, add a new `ItemGroup` (after the existing `ItemGroup` blocks, before `</Project>`):

```xml
  <ItemGroup>
    <None Update="Eval\boundary-corpus.json">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
```

- [ ] **Step 4: Write the loader sanity test**

Create `tests/AIHelperNET.Integration.Tests/Eval/CorpusLoaderTests.cs`:

```csharp
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class CorpusLoaderTests
{
    [Fact]
    public void Load_ReturnsNonEmptyCorpus_WithParsedEnums()
    {
        var entries = CorpusLoader.Load();

        entries.Should().NotBeEmpty();
        entries.Should().OnlyHaveUniqueItems(e => e.Id);
        entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.LatestItem.Text));
        // Every label is a defined enum value (parsing already enforced this; assert coverage breadth).
        entries.Select(e => e.ExpectedLabel).Distinct().Count().Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Load_CoversTheKeyScenarioCases()
    {
        var entries = CorpusLoader.Load();
        entries.Should().Contain(e => e.Id == "scenarioA-cache-invalidation"
            && e.ExpectedLabel == BoundaryLabel.QuestionContinued);
        entries.Should().Contain(e => e.Id == "scenarioB-different-topic"
            && e.ExpectedLabel == BoundaryLabel.NewQuestion);
    }
}
```

- [ ] **Step 5: Run the loader tests**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~CorpusLoaderTests"`
Expected: PASS (2 tests). If the file is not found, confirm the csproj copy entry from Step 3 and rebuild.

- [ ] **Step 6: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/CorpusEntry.cs tests/AIHelperNET.Integration.Tests/Eval/boundary-corpus.json tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj tests/AIHelperNET.Integration.Tests/Eval/CorpusLoaderTests.cs
git commit -m "test(eval): hand-authored boundary corpus + loader"
```

---

## Task 3: Heuristic regression test + baseline

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs`

- [ ] **Step 1: Write the heuristic eval test (baseline placeholder 0.0)**

Create `tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs`. Start with `Baseline = 0.0` so the test passes once; you will raise it to the measured value in Step 3.

```csharp
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

public class BoundaryHeuristicEvalTests(ITestOutputHelper output)
{
    // Set to the measured heuristic accuracy over the corpus — locks current behavior.
    private const double Baseline = 0.0;

    [Fact]
    public void Heuristic_MeetsAccuracyBaseline_OverCorpus()
    {
        var detector = new QuestionBoundaryDetector();
        var corpus = CorpusLoader.Load();
        var matrix = new ConfusionMatrix();

        foreach (var entry in corpus)
        {
            // recentQuestions mirrors the pipeline: prior Other-speaker texts.
            var recentQuestions = entry.RecentItems
                .Where(i => i.Speaker == Speaker.Other)
                .Select(i => i.Text)
                .ToList();

            var result = detector.Evaluate(
                entry.LatestItem.Text, entry.LatestItem.Speaker, entry.ActiveTurnStatus, recentQuestions);

            matrix.Record(entry.ExpectedLabel, result.Classification);
        }

        output.WriteLine(EvalReport.ToText(matrix, "Heuristic (QuestionBoundaryDetector)"));
        matrix.Accuracy.Should().BeGreaterThanOrEqualTo(Baseline);
    }
}
```

- [ ] **Step 2: Run the test and read the printed accuracy**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryHeuristicEvalTests" --logger "console;verbosity=detailed"`
Expected: PASS. In the output, find the line `Total=24 Accuracy=NN.N%` from the report. Note the accuracy value (e.g. `62.5%`).

- [ ] **Step 3: Lock the baseline to the measured value**

Edit the `Baseline` constant to the measured accuracy rounded DOWN to the nearest 0.05 (so normal authoring tweaks don't flake, but a real regression fails). Example: if measured `Accuracy=62.5%`, set:

```csharp
    private const double Baseline = 0.60;
```

(Use the actual measured value from Step 2, floored to a 0.05 step. Add a one-line comment with the exact measured number, e.g. `// measured 2026-06-09: 0.625`.)

- [ ] **Step 4: Re-run to confirm the locked baseline passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryHeuristicEvalTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/BoundaryHeuristicEvalTests.cs
git commit -m "test(eval): heuristic regression test with measured accuracy baseline"
```

---

## Task 4: Opt-in real-Haiku harness + skip-clean proof

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs`

This test runs the REAL Haiku classifier only when the env var `AIHELPER_AI_EVAL_KEY` holds an API key; otherwise it returns early (passing) without any network call, so CI is unaffected. It makes no accuracy assertion — the model's score is informational for the deferred tuning follow-up.

- [ ] **Step 1: Write the harness**

Create `tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs`:

```csharp
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Common;
using FluentResults;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

public class BoundaryClassifierAiEvalTests(ITestOutputHelper output)
{
    private const string KeyEnvVar = "AIHELPER_AI_EVAL_KEY";

    [Fact]
    public async Task RealHaiku_OverCorpus_ProducesReport()
    {
        var apiKey = Environment.GetEnvironmentVariable(KeyEnvVar);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            output.WriteLine($"Skipped: set {KeyEnvVar} to an Anthropic API key to run the real-Haiku eval.");
            return; // CI path — no network, passes trivially
        }

        var classifier = new QuestionBoundaryClassifier(
            new HttpClient(),
            new EnvSecretStore(apiKey),
            Options.Create(new ClaudeOptions()));

        var corpus = CorpusLoader.Load();
        var matrix = new ConfusionMatrix();
        var failures = 0;
        var misses = new List<string>();

        foreach (var entry in corpus)
        {
            var recentItems = entry.RecentItems
                .Select(i => TranscriptItem.Create(i.Speaker, i.Text, DateTimeOffset.UnixEpoch, 1.0f))
                .ToList();
            var latest = TranscriptItem.Create(
                entry.LatestItem.Speaker, entry.LatestItem.Text, DateTimeOffset.UnixEpoch, 1.0f);

            try
            {
                var result = await classifier.ClassifyAsync(
                    entry.ActiveTurnStatus, recentItems, latest, entry.LatestItem.Speaker, CancellationToken.None);
                matrix.Record(entry.ExpectedLabel, result.Classification);
                if (result.Classification != entry.ExpectedLabel)
                    misses.Add($"{entry.Id}: expected {entry.ExpectedLabel}, got {result.Classification}");
            }
            catch (Exception ex)
            {
                failures++;
                output.WriteLine($"API failure for {entry.Id}: {ex.Message}");
            }
        }

        var report = EvalReport.ToText(matrix, "Real Haiku (QuestionBoundaryClassifier)")
            + $"\nAPI failures: {failures}\nMisses:\n  " + string.Join("\n  ", misses);
        output.WriteLine(report);

        var reportPath = Path.Combine(AppPaths.DiagnosticsDir, $"ai-eval-{DateTime.UtcNow:yyyyMMdd-HHmmss}.txt");
        Directory.CreateDirectory(AppPaths.DiagnosticsDir);
        await File.WriteAllTextAsync(reportPath, report);
        output.WriteLine($"Report written to {reportPath}");
    }

    /// <summary>Minimal <see cref="ISecretStore"/> that yields a fixed key from the environment.</summary>
    private sealed class EnvSecretStore(string key) : ISecretStore
    {
        public Result<SecureString> GetApiKey()
        {
            var ss = new SecureString();
            foreach (var c in key) ss.AppendChar(c);
            ss.MakeReadOnly();
            return Result.Ok(ss);
        }

        public Result SaveApiKey(SecureString key) => Result.Ok();
        public Result DeleteApiKey() => Result.Ok();
        public bool HasApiKey() => true;
    }
}
```

- [ ] **Step 2: Run the harness with the env var unset (the CI path)**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~BoundaryClassifierAiEvalTests"`
Expected: PASS (1 test), with output line `Skipped: set AIHELPER_AI_EVAL_KEY ...`. No network call is made. This proves the gate.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Eval/BoundaryClassifierAiEvalTests.cs
git commit -m "test(eval): opt-in real-Haiku boundary eval harness (env-gated)"
```

---

## Task 5: Full verification

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on; note the Integration.Tests project sets `NoWarn` for CS1591/CA1707/CA1001).

- [ ] **Step 2: Run the Integration.Tests project**

Run: `dotnet test tests/AIHelperNET.Integration.Tests`
Expected: The new Eval tests pass. Pre-existing E2E SQLite "table is locked" flakiness may intermittently fail one E2E scenario under load — that is a known pre-existing issue (reproduces on `develop`), unrelated to this change. Re-run the failing E2E in isolation to confirm it is the lock flakiness and not an Eval failure.

- [ ] **Step 3: Run the deterministic unit projects**

Run: `dotnet test tests/AIHelperNET.Domain.Tests` then `dotnet test tests/AIHelperNET.Application.Tests`
Expected: all green (unchanged by this branch).

- [ ] **Step 4: Close the leftover Photos viewer** (if any UITest was triggered) per the project convention.

---

## Self-Review notes (already reconciled)

- **Spec coverage:** corpus (hand-authored, JSONL-aligned) → Task 2; pure ConfusionMatrix/EvalReport → Task 1; CI heuristic regression w/ measured baseline → Task 3; opt-in real-Haiku harness that skips cleanly in CI → Task 4; placement under `Integration.Tests/Eval/` → all tasks; testing (pure-core unit tests, corpus sanity, regression, skip-proof) → Tasks 1/2/3/4. No tuning/threshold/agreement-guard/converter work (out of scope) — none present.
- **Type consistency:** `ConfusionMatrix.Record/Count/Accuracy/PrecisionFor/RecallFor`, `EvalReport.ToText(matrix, title)`, `CorpusEntry`/`CorpusItem`/`CorpusLoader.Load()`, and the real signatures `QuestionBoundaryDetector.Evaluate(text, speaker, status, recentQuestions)` / `QuestionBoundaryClassifier.ClassifyAsync(status, recentItems, latestItem, speaker, ct)` all match across tasks and the production code.
- **No placeholders:** the corpus JSON is complete; the only intentionally-deferred value is the `Baseline` const, which Task 3 measures and locks with explicit steps.

## Notes for the executor

- One env var, `AIHELPER_AI_EVAL_KEY`, both enables the AI harness and supplies the key (simpler than the spec's two-token sketch; same effect — CI never runs it).
- Do not touch the parked `git stash@{0}` or the `.claude/worktrees` staged deletion.
- GitFlow: this work is on `feature/boundary-eval-harness`; PR to `develop` at the end via finishing-a-development-branch.
```
