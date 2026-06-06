# Question Detection Improvement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the first-word heuristic question detector with a two-stage pipeline — cheap pre-filter followed by Claude Haiku classification — to eliminate false positives and catch split-sentence continuations.

**Architecture:** `SegmentAccumulator` buffers consecutive "Other" channel segments within a 3-second gap, then a pre-filter (min 4 words + `QuestionDetector`) gates whether to call `HaikuQuestionClassifier`. Haiku returns `NewQuestion`, `Continuation`, or `NotAQuestion`. `Continuation` appends to the active turn's question text and re-generates the answer.

**Tech Stack:** .NET 10, xUnit, NSubstitute, FluentAssertions, Anthropic Claude Haiku API (non-streaming POST), System.Text.Json

---

## File Map

| Action | File |
|--------|------|
| Create | `src/AIHelperNET.Application/Abstractions/IQuestionClassifier.cs` |
| Create | `src/AIHelperNET.Application/Sessions/SegmentAccumulator.cs` |
| Create | `src/AIHelperNET.Infrastructure/AI/HaikuQuestionClassifier.cs` |
| Modify | `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` |
| Modify | `src/AIHelperNET.Domain/Questions/QuestionDetector.cs` |
| Modify | `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs` |
| Modify | `src/AIHelperNET.Infrastructure/DependencyInjection.cs` |
| Modify | `src/AIHelperNET.App/Services/SessionRunner.cs` |
| Create | `tests/AIHelperNET.Application.Tests/Sessions/SegmentAccumulatorTests.cs` |
| Modify | `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` |
| Modify | `tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs` |
| Create | `tests/AIHelperNET.Infrastructure.Tests/AI/HaikuQuestionClassifierTests.cs` |

---

## Task 1: IQuestionClassifier Port

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IQuestionClassifier.cs`

- [ ] **Step 1: Create the interface and enum**

```csharp
// src/AIHelperNET.Application/Abstractions/IQuestionClassifier.cs
namespace AIHelperNET.Application.Abstractions;

public enum ClassificationResult { NewQuestion, Continuation, NotAQuestion }

public interface IQuestionClassifier
{
    Task<ClassificationResult> ClassifyAsync(
        string combinedText,
        IReadOnlyList<string> recentQuestions,
        CancellationToken ct);
}
```

- [ ] **Step 2: Build to verify it compiles**

```
dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Application/Abstractions/IQuestionClassifier.cs
git commit -m "feat: add IQuestionClassifier port and ClassificationResult enum"
```

---

## Task 2: SegmentAccumulator + Tests

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/SegmentAccumulator.cs`
- Create: `tests/AIHelperNET.Application.Tests/Sessions/SegmentAccumulatorTests.cs`

- [ ] **Step 1: Write the failing tests first**

```csharp
// tests/AIHelperNET.Application.Tests/Sessions/SegmentAccumulatorTests.cs
using AIHelperNET.Application.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SegmentAccumulatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Add_SingleSegment_ReturnsNull_BuffersIt()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Hello there", T0).Should().BeNull();
    }

    [Fact]
    public void Add_TwoSegmentsWithin3s_ReturnsNull_BothBuffered()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Can we use them", T0).Should().BeNull();
        sut.Add("and what is the difference", T0.AddSeconds(1)).Should().BeNull();
    }

    [Fact]
    public void Add_ThirdSegmentBeyond3sGap_FlushesFirstTwo_StartsNewBuffer()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Can we use them", T0);
        sut.Add("and what is the difference", T0.AddSeconds(1));

        var flushed = sut.Add("Next question", T0.AddSeconds(5));

        flushed.Should().Be("Can we use them and what is the difference");
    }

    [Fact]
    public void Add_SecondSegmentBeyond3sGap_FlushesFirst()
    {
        var sut = new SegmentAccumulator();
        sut.Add("First question", T0);

        var result = sut.Add("Second question", T0.AddSeconds(4));

        result.Should().Be("First question");
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsNull()
    {
        var sut = new SegmentAccumulator();
        sut.Flush().Should().BeNull();
    }

    [Fact]
    public void Flush_NonEmptyBuffer_ReturnsCombinedText_ClearsBuffer()
    {
        var sut = new SegmentAccumulator();
        sut.Add("First", T0);
        sut.Add("Second", T0.AddSeconds(1));

        var result = sut.Flush();

        result.Should().Be("First Second");
        sut.Flush().Should().BeNull();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SegmentAccumulator" --no-build
```
Expected: Build error — `SegmentAccumulator` does not exist yet.

- [ ] **Step 3: Implement SegmentAccumulator**

```csharp
// src/AIHelperNET.Application/Sessions/SegmentAccumulator.cs
namespace AIHelperNET.Application.Sessions;

public sealed class SegmentAccumulator
{
    private const int GapThresholdSeconds = 3;

    private readonly List<string> _buffer = [];
    private DateTimeOffset _lastTimestamp;

    /// <summary>
    /// Adds a segment. Returns the combined buffered text if the gap since the last
    /// segment exceeds GapThresholdSeconds, otherwise returns null (segment buffered).
    /// </summary>
    public string? Add(string text, DateTimeOffset timestamp)
    {
        if (_buffer.Count > 0 &&
            (timestamp - _lastTimestamp).TotalSeconds > GapThresholdSeconds)
        {
            var flushed = string.Join(" ", _buffer);
            _buffer.Clear();
            _buffer.Add(text);
            _lastTimestamp = timestamp;
            return flushed;
        }

        _buffer.Add(text);
        _lastTimestamp = timestamp;
        return null;
    }

    /// <summary>Force-flushes the current buffer. Returns null if empty.</summary>
    public string? Flush()
    {
        if (_buffer.Count == 0) return null;
        var result = string.Join(" ", _buffer);
        _buffer.Clear();
        return result;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SegmentAccumulator"
```
Expected: 6 tests passed.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Application/Sessions/SegmentAccumulator.cs
git add tests/AIHelperNET.Application.Tests/Sessions/SegmentAccumulatorTests.cs
git commit -m "feat: add SegmentAccumulator to buffer consecutive Other-channel segments"
```

---

## Task 3: QuestionDetector Hardening + Tests

**Files:**
- Modify: `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`
- Modify: `tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these test cases to `QuestionDetectorTests.cs`, inside the existing `QuestionDetectorTests` class:

```csharp
[Theory]
[InlineData("Software engineering, system design, coding.")]
[InlineData("Software engineering, system design, data structure, coding.")]
[InlineData("Software engineering, system design, software engineering, software engineering.")]
public void Evaluate_HallucinationPhrase_NotAQuestion(string text)
{
    _sut.Evaluate(text, []).IsQuestion.Should().BeFalse();
}

[Theory]
[InlineData("So.")]
[InlineData("What?")]
[InlineData("Go on.")]
[InlineData("Can you")]
public void Evaluate_FewerThan4Words_NotAQuestion(string text)
{
    _sut.Evaluate(text, []).IsQuestion.Should().BeFalse();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~QuestionDetector"
```
Expected: The new tests fail (existing tests still pass).

- [ ] **Step 3: Update QuestionDetector**

Replace the full contents of `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`:

```csharp
namespace AIHelperNET.Domain.Questions;

/// <summary>Detects whether a transcript segment contains an interview question and deduplicates against recent questions.</summary>
public sealed class QuestionDetector
{
    private const double DuplicateThreshold = 0.6;
    private const int MinWords = 4;

    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "what","why","how","when","where","which","who","can","could","would",
        "will","do","does","did","is","are","should"
    };

    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain","describe","write","implement","design","compare","optimize",
        "refactor","debug","walk","tell","give","show"
    };

    // Whisper hallucination phrases that start with interrogative words and would otherwise pass.
    // Compared after stripping punctuation and lowercasing.
    private static readonly HashSet<string> NoisePhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "software engineering system design coding",
        "software engineering system design",
        "software engineering system design data structure coding",
        "software engineering system design software engineering",
        "software engineering system design algorithms data structures coding",
    };

#pragma warning disable CA1822
    public QuestionDetectionResult Evaluate(string text, IReadOnlyCollection<string> recentQuestions)
#pragma warning restore CA1822
    {
        if (string.IsNullOrWhiteSpace(text))
            return QuestionDetectionResult.NotAQuestion();

        var normalized = text.Trim();

        if (IsNoisePhrase(normalized))
            return QuestionDetectionResult.NotAQuestion();

        if (!LooksLikeQuestion(normalized))
            return QuestionDetectionResult.NotAQuestion();

        var candidateTokens = Tokenize(normalized);
        foreach (var prior in recentQuestions)
        {
            if (Jaccard(candidateTokens, Tokenize(prior)) >= DuplicateThreshold)
                return QuestionDetectionResult.Duplicate();
        }
        return QuestionDetectionResult.NewQuestion(normalized);
    }

    private static bool LooksLikeQuestion(string text)
    {
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
            return false;
        if (text.EndsWith('?')) return true;
        var first = FirstWord(text);
        return Interrogatives.Contains(first) || ImperativeVerbs.Contains(first);
    }

    private static bool IsNoisePhrase(string text)
    {
        var stripped = new string(
            text.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .ToArray())
            .Trim();
        // Also try collapsing repeated words (e.g. "software engineering software engineering")
        return NoisePhrases.Contains(stripped) ||
               NoisePhrases.Any(p => stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\t'],
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

    private static string FirstWord(string text)
    {
        var idx = text.IndexOf(' ');
        return (idx < 0 ? text : text[..idx]).Trim('?', '.', ',', '!');
    }
}
```

- [ ] **Step 4: Run all QuestionDetector tests**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~QuestionDetector"
```
Expected: All tests pass (existing + new).

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Domain/Questions/QuestionDetector.cs
git add tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs
git commit -m "feat: harden QuestionDetector with 4-word minimum and hallucination phrase filter"
```

---

## Task 4: ConversationTurn.AppendToQuestion Domain Method

**Files:**
- Modify: `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs`

The `InitialQuestionText` property is currently get-only. `Continuation` classification needs to append the follow-up segment to the question text before re-generating the answer.

- [ ] **Step 1: Write the failing test**

Add to `tests/AIHelperNET.Domain.Tests/` — create a new file `Sessions/ConversationTurnTests.cs`:

```csharp
// tests/AIHelperNET.Domain.Tests/Sessions/ConversationTurnTests.cs
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class ConversationTurnTests
{
    private static ConversationTurn MakeTurn(string question = "Initial question text here?")
        => ConversationTurn.Create(
            SessionId.New(), QuestionId.New(), question, DateTimeOffset.UnixEpoch);

    [Fact]
    public void AppendToQuestion_AppendsTextWithSpace()
    {
        var turn = MakeTurn("Can we use them");
        turn.AppendToQuestion("and what is the difference?");
        turn.InitialQuestionText.Should().Be("Can we use them and what is the difference?");
    }

    [Fact]
    public void AppendToQuestion_UpdatesUpdatedAt()
    {
        var turn = MakeTurn();
        var before = turn.UpdatedAt;
        turn.AppendToQuestion("extra");
        turn.UpdatedAt.Should().BeAfter(before);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~ConversationTurnTests"
```
Expected: Compile error — `AppendToQuestion` does not exist.

- [ ] **Step 3: Add AppendToQuestion to ConversationTurn**

In `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs`:

Change line 42:
```csharp
public string InitialQuestionText { get; }
```
To:
```csharp
public string InitialQuestionText { get; private set; }
```

Then add this method after `AttachClarificationResponse`:

```csharp
/// <summary>Appends a continuation segment to the initial question text.</summary>
/// <param name="continuation">The additional text to append.</param>
public void AppendToQuestion(string continuation)
{
    InitialQuestionText = InitialQuestionText + " " + continuation;
    UpdatedAt = DateTimeOffset.UtcNow;
}
```

- [ ] **Step 4: Run tests**

```
dotnet test tests/AIHelperNET.Domain.Tests --filter "FullyQualifiedName~ConversationTurnTests"
```
Expected: 2 tests passed.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Domain/Sessions/ConversationTurn.cs
git add tests/AIHelperNET.Domain.Tests/Sessions/ConversationTurnTests.cs
git commit -m "feat: add ConversationTurn.AppendToQuestion for continuation handling"
```

---

## Task 5: HaikuQuestionClassifier + Tests

**Files:**
- Create: `src/AIHelperNET.Infrastructure/AI/HaikuQuestionClassifier.cs`
- Create: `tests/AIHelperNET.Infrastructure.Tests/AI/HaikuQuestionClassifierTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AIHelperNET.Infrastructure.Tests/AI/HaikuQuestionClassifierTests.cs
using System.Net;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.AI;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public class HaikuQuestionClassifierTests
{
    private static HaikuQuestionClassifier MakeSut(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };

        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));

        var options = Options.Create(new ClaudeOptions());
        return new HaikuQuestionClassifier(http, secrets, options);
    }

    private static string MakeResponse(string text) =>
        $$"""{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"{{text}}"}],"model":"claude-haiku","stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":1}}""";

    [Theory]
    [InlineData("NewQuestion",   ClassificationResult.NewQuestion)]
    [InlineData("Continuation",  ClassificationResult.Continuation)]
    [InlineData("NotAQuestion",  ClassificationResult.NotAQuestion)]
    [InlineData("garbage text",  ClassificationResult.NotAQuestion)]
    [InlineData("",              ClassificationResult.NotAQuestion)]
    public async Task ClassifyAsync_ParsesApiResponse(string apiText, ClassificationResult expected)
    {
        var sut = MakeSut(MakeResponse(apiText));
        var result = await sut.ClassifyAsync("How do you handle DI?", [], CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ClassifyAsync_ApiError_ReturnsNotAQuestion()
    {
        var sut = MakeSut("""{"error":{"type":"auth_error"}}""", HttpStatusCode.Unauthorized);
        var result = await sut.ClassifyAsync("test", [], CancellationToken.None);
        Assert.Equal(ClassificationResult.NotAQuestion, result);
    }

    [Fact]
    public async Task ClassifyAsync_MalformedJson_ReturnsNotAQuestion()
    {
        var sut = MakeSut("not json at all");
        var result = await sut.ClassifyAsync("test", [], CancellationToken.None);
        Assert.Equal(ClassificationResult.NotAQuestion, result);
    }

    [Fact]
    public async Task ClassifyAsync_IncludesRecentQuestionsInRequest()
    {
        var handler = new CapturingHttpMessageHandler(MakeResponse("NewQuestion"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "k") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));
        var sut = new HaikuQuestionClassifier(http, secrets, Options.Create(new ClaudeOptions()));

        await sut.ClassifyAsync("new question", ["Q1?", "Q2?"], CancellationToken.None);

        Assert.Contains("Q1?", handler.LastRequestBody);
        Assert.Contains("Q2?", handler.LastRequestBody);
    }
}

file sealed class MockHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}

file sealed class CapturingHttpMessageHandler(string body) : HttpMessageHandler
{
    public string LastRequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestBody = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(""));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
```

> **Note:** The `Infrastructure.Tests` project currently uses xUnit without NSubstitute. Check the `.csproj` — if NSubstitute is not referenced, add it: `dotnet add tests/AIHelperNET.Infrastructure.Tests package NSubstitute`.

- [ ] **Step 2: Add NSubstitute to Infrastructure tests if missing**

```
dotnet list tests/AIHelperNET.Infrastructure.Tests package
```
If `NSubstitute` is absent:
```
dotnet add tests/AIHelperNET.Infrastructure.Tests package NSubstitute
```

- [ ] **Step 3: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~HaikuQuestionClassifier" --no-build
```
Expected: Compile error — `HaikuQuestionClassifier` does not exist.

- [ ] **Step 4: Implement HaikuQuestionClassifier**

```csharp
// src/AIHelperNET.Infrastructure/AI/HaikuQuestionClassifier.cs
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

public sealed class HaikuQuestionClassifier(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IQuestionClassifier
{
    private const string HaikuModel = "claude-haiku-4-5-20251001";

    private const string SystemPrompt =
        "You are classifying speech segments from a live technical interview. " +
        "Reply with exactly one word — no punctuation, no explanation: " +
        "NewQuestion if this is a new interview question, " +
        "Continuation if it continues or completes the previous question, " +
        "NotAQuestion if it is not a question at all.";

    public async Task<ClassificationResult> ClassifyAsync(
        string combinedText,
        IReadOnlyList<string> recentQuestions,
        CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
        {
            Log.Warning("HaikuClassifier: no API key configured, returning NotAQuestion");
            return ClassificationResult.NotAQuestion;
        }

        var opts = options.Value;
        var contextPart = recentQuestions.Count > 0
            ? "\nRecent questions for context: " + string.Join("; ", recentQuestions)
            : string.Empty;

        var body = JsonSerializer.Serialize(new
        {
            model = HaikuModel,
            max_tokens = 10,
            stream = false,
            system = SystemPrompt + contextPart,
            messages = new[] { new { role = "user", content = combinedText } }
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");

        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("HaikuClassifier: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return ClassificationResult.NotAQuestion;
            }

            return ParseResponse(json);
        }
        finally
        {
            if (apiKey.Length > 0)
            {
                unsafe
                {
                    fixed (char* p = apiKey)
                        for (int i = 0; i < apiKey.Length; i++) p[i] = '\0';
                }
            }
        }
    }

    private static ClassificationResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim() ?? string.Empty;

            Log.Debug("HaikuClassifier: response = {Text}", text);

            return text switch
            {
                "NewQuestion"  => ClassificationResult.NewQuestion,
                "Continuation" => ClassificationResult.Continuation,
                _              => ClassificationResult.NotAQuestion,
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "HaikuClassifier: failed to parse response");
            return ClassificationResult.NotAQuestion;
        }
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
```

- [ ] **Step 5: Run tests**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~HaikuQuestionClassifier"
```
Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.Infrastructure/AI/HaikuQuestionClassifier.cs
git add tests/AIHelperNET.Infrastructure.Tests/AI/HaikuQuestionClassifierTests.cs
git commit -m "feat: add HaikuQuestionClassifier for LLM-based question classification"
```

---

## Task 6: Refactor TranscriptPipelineService + Tests

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Modify: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

- [ ] **Step 1: Rewrite the tests first**

Replace the full contents of `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TranscriptPipelineServiceTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;
    private static readonly DateTimeOffset T0Plus4s = T0.AddSeconds(4); // triggers accumulator flush

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

    private static TranscriptItem MakeItem(Speaker speaker, string text, DateTimeOffset? at = null)
        => TranscriptItem.Create(speaker, text, at ?? T0, 0.9f);

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink, IUnitOfWork uow)
        MakeSvc(ITranscriptSink sink, IQuestionClassifier? classifier = null)
    {
        // Default classifier: NewQuestion for any text that passes pre-filter
        classifier ??= Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.NewQuestion));

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

        return (new TranscriptPipelineService(factory, sink, turnSink, classifier), mediator, turnSink, uow);
    }

    // Helper: process a segment, then flush the accumulator (simulates the 3s gap firing)
    private static async Task ProcessAndFlushAsync(
        TranscriptPipelineService svc, Session session, TranscriptItem item, IUnitOfWork uow)
    {
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);
        await svc.FlushAccumulatorAsync(session, uow, CancellationToken.None);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);

        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_NoTurnCreated()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await ProcessAndFlushAsync(svc, session, item, uow);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsNotAQuestion_NoTurnCreated()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.NotAQuestion));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you approach this problem?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsContinuation_WithActiveTurn_AppendsAndRegenerates()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();

        var classifier = Substitute.For<IQuestionClassifier>();
        // First call: NewQuestion (creates the turn)
        // Second call: Continuation (appends to turn)
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(ClassificationResult.NewQuestion),
                Task.FromResult(ClassificationResult.Continuation));
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink, classifier);

        // First segment creates a turn
        var first = MakeItem(Speaker.Other, "Can we use observables and what is");
        await ProcessAndFlushAsync(svc, session, first, uow);
        session.ConversationTurns.Should().HaveCount(1);

        // Second segment is a continuation — should append, not create a new turn
        var second = MakeItem(Speaker.Other, "the difference comparing them to promise");
        await ProcessAndFlushAsync(svc, session, second, uow);

        session.ConversationTurns.Should().HaveCount(1, "Continuation must not create a new turn");
        session.ConversationTurns[0].InitialQuestionText.Should()
            .Contain("the difference comparing them to promise");

        await Task.Delay(200);
        // Two GenerateAnswerCommand calls: once for NewQuestion, once for Continuation
        await mediator.Received(2).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierReturnsContinuation_NoActiveTurn_PromotesToNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.Continuation));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you approach this kind of problem?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        // No active turn → Continuation promoted to NewQuestion
        session.ConversationTurns.Should().HaveCount(1);
    }

    [Fact]
    public async Task OtherSpeaker_ClassifierThrows_FallsBackToNewQuestion()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        classifier.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns<Task<ClassificationResult>>(_ => throw new HttpRequestException("network error"));
        var (svc, mediator, _, uow) = MakeSvc(transcriptSink, classifier);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        // Falls back: QuestionDetector said yes, so treat as NewQuestion
        session.ConversationTurns.Should().HaveCount(1);
        await Task.Delay(200);
        await mediator.Received(1).Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TwoSegmentsWithin3s_CombinedAndClassifiedTogether()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var classifier = Substitute.For<IQuestionClassifier>();
        string? capturedText = null;
        classifier.ClassifyAsync(Arg.Do<string>(t => capturedText = t),
                Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(ClassificationResult.NewQuestion));
        var (svc, _, _, uow) = MakeSvc(transcriptSink, classifier);

        // Two segments 1s apart — accumulator should combine them
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "Can we use them", T0), uow, CancellationToken.None);
        await svc.ProcessAsync(session, MakeItem(Speaker.Other, "and what is", T0.AddSeconds(1)), uow, CancellationToken.None);
        await svc.FlushAccumulatorAsync(session, uow, CancellationToken.None);

        capturedText.Should().Be("Can we use them and what is");
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, T0);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", T0).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, uow, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task TranscriptSink_CalledForEveryItem()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello there friend");
        await svc.ProcessAsync(session, item, uow, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_NotifiesConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await ProcessAndFlushAsync(svc, session, item, uow);

        var expectedId = session.ConversationTurns[0].Id;
        turnSink.Received(1).OnTurnCreated(expectedId, Arg.Any<string>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_DoesNotNotifyConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink, uow) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await ProcessAndFlushAsync(svc, session, item, uow);

        turnSink.DidNotReceive().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineService"
```
Expected: Compile error — `TranscriptPipelineService` constructor does not accept `IQuestionClassifier` yet.

- [ ] **Step 3: Rewrite TranscriptPipelineService**

Replace the full contents of `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AIHelperNET.Application.Sessions;

/// <summary>Processes incoming transcript items and drives conversation turn lifecycle.</summary>
public sealed class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink,
    IQuestionClassifier classifier)
{
    private readonly QuestionDetector _detector = new();
    private readonly SegmentAccumulator _accumulator = new();

    /// <summary>Processes a single transcript item against the active session.</summary>
    public async Task ProcessAsync(Session session, TranscriptItem item, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        GenerateAnswerCommand? pendingCommand = null;

        if (item.Speaker == Speaker.Other)
        {
            var combined = _accumulator.Add(item.Text, item.Timestamp);
            if (combined is not null)
                pendingCommand = await BuildCommandForCombinedAsync(session, combined, item.Timestamp, ct);
        }
        else if (item.Speaker == Speaker.Me &&
                 session.ActiveTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = _detector.Evaluate(item.Text, session.Questions.Select(q => q.Text).ToList());
            if (detection.IsQuestion)
            {
                session.ActiveTurn.AttachClarificationQuestion(item.Id);
                session.ActiveTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);

        if (pendingCommand is not null)
            FireAndForget(pendingCommand, ct);
    }

    /// <summary>Drains the accumulator buffer — call on session stop to process any remaining buffered segments.</summary>
    public async Task FlushAccumulatorAsync(Session session, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var combined = _accumulator.Flush();
        if (combined is null) return;

        var cmd = await BuildCommandForCombinedAsync(session, combined, DateTimeOffset.UtcNow, ct);
        await unitOfWork.SaveChangesAsync(ct);

        if (cmd is not null)
            FireAndForget(cmd, ct);
    }

    private async Task<GenerateAnswerCommand?> BuildCommandForCombinedAsync(
        Session session, string combined, DateTimeOffset timestamp, CancellationToken ct)
    {
        var recentTexts = session.Questions.Select(q => q.Text).ToList();

        // Pre-filter: quick heuristic check before making a Haiku API call.
        var preFilter = _detector.Evaluate(combined, recentTexts);
        if (!preFilter.IsQuestion || preFilter.IsDuplicate)
            return null;

        // LLM classification.
        ClassificationResult classification;
        try
        {
            classification = await classifier.ClassifyAsync(
                combined, recentTexts.TakeLast(2).ToList(), ct);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "HaikuClassifier: call failed, falling back to NewQuestion for: {Text}",
                combined[..Math.Min(80, combined.Length)]);
            classification = ClassificationResult.NewQuestion;
        }

        return classification switch
        {
            ClassificationResult.NewQuestion  => HandleNewQuestion(session, combined, timestamp),
            ClassificationResult.Continuation => HandleContinuation(session, combined),
            _                                 => null,
        };
    }

    private GenerateAnswerCommand? HandleNewQuestion(Session session, string combined, DateTimeOffset timestamp)
    {
        var q = DetectedQuestion.Create(combined, QuestionSource.Audio, timestamp);
        session.AddDetectedQuestion(q);
        var turnResult = session.AddConversationTurn(q.Id, combined, timestamp);
        if (!turnResult.IsSuccess) return null;

        var turn = turnResult.Value;
        turnSink.OnTurnCreated(turn.Id, combined);
        return new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary);
    }

    private GenerateAnswerCommand? HandleContinuation(Session session, string combined)
    {
        var activeTurn = session.ActiveTurn;

        if (activeTurn is null
            || activeTurn.Status == ConversationTurnStatus.AwaitingClarification
            || activeTurn.Status == ConversationTurnStatus.Dismissed
            || activeTurn.Status == ConversationTurnStatus.Resolved)
        {
            // No open turn — promote to NewQuestion.
            return HandleNewQuestion(session, combined, DateTimeOffset.UtcNow);
        }

        activeTurn.AppendToQuestion(combined);
        return new GenerateAnswerCommand(session.Id, activeTurn.Id, AnswerVersionType.Preliminary);
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            using var scope  = scopeFactory.CreateScope();
            var mediator     = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(command, ct);
        }, ct);
    }
}
```

- [ ] **Step 4: Build to catch any compile errors**

```
dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj
```
Fix any type mismatches or missing members. The `QuestionId.AsTranscriptItemId()` or `new TranscriptItemId(q.Id.Value)` issue will surface here — adapt to the actual domain ID types.

- [ ] **Step 5: Run TranscriptPipelineService tests**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineService"
```
Expected: All tests pass.

- [ ] **Step 6: Run full test suite**

```
dotnet test
```
Expected: All tests pass.

- [ ] **Step 7: Commit**

```
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs
git add tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat: refactor TranscriptPipelineService to use SegmentAccumulator and IQuestionClassifier"
```

---

## Task 7: DI Registration + SessionRunner Flush

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`

- [ ] **Step 1: Register HaikuQuestionClassifier in Infrastructure DI**

In `src/AIHelperNET.Infrastructure/DependencyInjection.cs`, add after the `ClaudeAnswerProvider` registrations:

```csharp
// After: services.AddSingleton<IAnswerProviderResolver, AnswerProviderResolver>();
services.AddHttpClient<HaikuQuestionClassifier>();
services.AddSingleton<IQuestionClassifier, HaikuQuestionClassifier>();
```

Also add the using at the top if not already present:
```csharp
using AIHelperNET.Application.Abstractions;
```

- [ ] **Step 2: Add FlushAccumulatorAsync call in SessionRunner**

In `src/AIHelperNET.App/Services/SessionRunner.cs`, inside `RunAsync`, find the consumer task's `finally` block (around line 211):

```csharp
finally
{
    await FlushAsync(); // drain whatever remains
}
```

Change it to:

```csharp
finally
{
    await FlushAsync(); // drain whatever remains
    await pipeline.FlushAccumulatorAsync(session, uow, CancellationToken.None);
}
```

- [ ] **Step 3: Build the full solution**

```
dotnet build
```
Expected: Build succeeded, 0 errors, 0 warnings that weren't already there.

- [ ] **Step 4: Run full test suite**

```
dotnet test
```
Expected: All tests pass.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs
git add src/AIHelperNET.App/Services/SessionRunner.cs
git commit -m "feat: register HaikuQuestionClassifier and flush accumulator on session stop"
```

---

## Task 8: End-to-End Validation

**Files:** None — validation only.

After all code changes, run the app and verify the full pipeline works.

- [ ] **Step 1: Build the app**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
```
Expected: 0 errors.

- [ ] **Step 2: Launch the app**

Use the `run-aihelper` skill or run directly:
```
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj
```

- [ ] **Step 3: Test audio mode**
  - Press `Ctrl+Shift+Space` to start a session
  - Speak a question via loopback or mic
  - Verify: transcript appears, a turn card is created, answer streams in
  - Verify: short filler phrases ("OK great", "I see") do NOT create turns

- [ ] **Step 4: Test screen capture**
  - Open a file from `D:\work\AIHelperNET\tests\testImage\` in Windows Photos
  - Press `Ctrl+Shift+S`
  - Verify: OCR reads the image content and a turn is created with an answer

- [ ] **Step 5: Test settings**
  - Open Settings (`Ctrl+Shift+Space` → gear icon)
  - Change Whisper model, language, API key
  - Save and restart — verify settings persist

- [ ] **Step 6: Final commit (if any fixes were needed)**

```
git add -p   # stage only intentional changes
git commit -m "fix: e2e validation fixes for question detection pipeline"
```
