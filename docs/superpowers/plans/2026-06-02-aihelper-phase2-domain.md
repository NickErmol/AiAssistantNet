# AIHelperNET — Phase 2: Domain Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 1 complete (solution skeleton green).

**Goal:** Implement the entire Domain layer — IDs, enums, value objects, entities, Session aggregate, QuestionDetector — with full TDD and zero NuGet dependencies.

**Architecture:** Pure C#, no NuGet packages. Rich model with factory methods returning `DomainResult<T>`. All tests in `AIHelperNET.Domain.Tests` with no mocks.

**Tech Stack:** .NET 10 (net10.0), xUnit 2.x, FluentAssertions 7.x

---

### Task 6: DomainResult — lightweight result type

**Files:**
- Create: `src/AIHelperNET.Domain/Common/DomainResult.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Common/DomainResultTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/AIHelperNET.Domain.Tests/Common/DomainResultTests.cs
using AIHelperNET.Domain.Common;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Common;

public class DomainResultTests
{
    [Fact]
    public void Ok_IsSuccess_True()
    {
        var r = DomainResult.Ok();
        r.IsSuccess.Should().BeTrue();
        r.IsFailed.Should().BeFalse();
    }

    [Fact]
    public void Fail_IsSuccess_False()
    {
        var r = DomainResult.Fail("bad");
        r.IsSuccess.Should().BeFalse();
        r.IsFailed.Should().BeTrue();
        r.Error.Should().Be("bad");
    }

    [Fact]
    public void OkT_CarriesValue()
    {
        var r = DomainResult.Ok(42);
        r.IsSuccess.Should().BeTrue();
        r.Value.Should().Be(42);
    }

    [Fact]
    public void FailT_HasError()
    {
        var r = DomainResult.Fail<int>("oops");
        r.IsFailed.Should().BeTrue();
        r.Error.Should().Be("oops");
    }
}
```

- [ ] **Step 2: Run — should fail (type not found)**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: compile error — `DomainResult` does not exist.

- [ ] **Step 3: Implement DomainResult**

```csharp
// src/AIHelperNET.Domain/Common/DomainResult.cs
namespace AIHelperNET.Domain.Common;

public readonly struct DomainResult
{
    public bool IsSuccess { get; }
    public bool IsFailed => !IsSuccess;
    public string Error { get; }

    private DomainResult(bool success, string error = "")
    {
        IsSuccess = success;
        Error = error;
    }

    public static DomainResult Ok() => new(true);
    public static DomainResult Fail(string error) => new(false, error);
    public static DomainResult<T> Ok<T>(T value) => DomainResult<T>.Ok(value);
    public static DomainResult<T> Fail<T>(string error) => DomainResult<T>.Fail(error);
}

public readonly struct DomainResult<T>
{
    public bool IsSuccess { get; }
    public bool IsFailed => !IsSuccess;
    public string Error { get; }
    private readonly T? _value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot access Value of a failed result: {Error}");

    private DomainResult(bool success, T? value, string error)
    {
        IsSuccess = success;
        _value = value;
        Error = error;
    }

    public static DomainResult<T> Ok(T value) => new(true, value, "");
    public static DomainResult<T> Fail(string error) => new(false, default, error);
}
```

- [ ] **Step 4: Run — should pass**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/AIHelperNET.Domain/Common/DomainResult.cs tests/AIHelperNET.Domain.Tests/Common/DomainResultTests.cs
git commit -m "feat(domain): add DomainResult<T> value type"
```

---

### Task 7: Enums

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/SessionState.cs`
- Create: `src/AIHelperNET.Domain/Sessions/Speaker.cs`
- Create: `src/AIHelperNET.Domain/Sessions/QuestionSource.cs`
- Create: `src/AIHelperNET.Domain/Sessions/AnswerStatus.cs`

No tests needed — enums are pure declarations verified by compilation.

- [ ] **Step 1: Create enum files**

```csharp
// src/AIHelperNET.Domain/Sessions/SessionState.cs
namespace AIHelperNET.Domain.Sessions;
public enum SessionState { Active, Stopped }
```

```csharp
// src/AIHelperNET.Domain/Sessions/Speaker.cs
namespace AIHelperNET.Domain.Sessions;
public enum Speaker { Me, Other }
```

```csharp
// src/AIHelperNET.Domain/Sessions/QuestionSource.cs
namespace AIHelperNET.Domain.Sessions;
public enum QuestionSource { Audio, Ocr, Manual }
```

```csharp
// src/AIHelperNET.Domain/Sessions/AnswerStatus.cs
namespace AIHelperNET.Domain.Sessions;
public enum AnswerStatus { Streaming, Completed, Cancelled, Failed }
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Domain/
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Domain/Sessions/
git commit -m "feat(domain): add domain enums"
```

---

### Task 8: Strongly-typed IDs

**Files:**
- Create: `src/AIHelperNET.Domain/Ids/SessionId.cs`
- Create: `src/AIHelperNET.Domain/Ids/TranscriptItemId.cs`
- Create: `src/AIHelperNET.Domain/Ids/QuestionId.cs`
- Create: `src/AIHelperNET.Domain/Ids/AnswerId.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Ids/IdTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Domain.Tests/Ids/IdTests.cs
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Ids;

public class IdTests
{
    [Fact]
    public void SessionId_New_IsUnique()
    {
        var a = SessionId.New();
        var b = SessionId.New();
        a.Should().NotBe(b);
    }

    [Fact]
    public void SessionId_EqualityByValue()
    {
        var g = Guid.CreateVersion7();
        new SessionId(g).Should().Be(new SessionId(g));
    }

    [Fact]
    public void QuestionId_NotAssignableToSessionId()
    {
        // Compile-time guarantee — this test just ensures the types exist and are distinct record structs.
        typeof(QuestionId).Should().NotBe(typeof(SessionId));
    }
}
```

- [ ] **Step 2: Run — compile error expected**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Implement IDs**

```csharp
// src/AIHelperNET.Domain/Ids/SessionId.cs
namespace AIHelperNET.Domain.Ids;
public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
}
```

```csharp
// src/AIHelperNET.Domain/Ids/TranscriptItemId.cs
namespace AIHelperNET.Domain.Ids;
public readonly record struct TranscriptItemId(Guid Value)
{
    public static TranscriptItemId New() => new(Guid.CreateVersion7());
}
```

```csharp
// src/AIHelperNET.Domain/Ids/QuestionId.cs
namespace AIHelperNET.Domain.Ids;
public readonly record struct QuestionId(Guid Value)
{
    public static QuestionId New() => new(Guid.CreateVersion7());
}
```

```csharp
// src/AIHelperNET.Domain/Ids/AnswerId.cs
namespace AIHelperNET.Domain.Ids;
public readonly record struct AnswerId(Guid Value)
{
    public static AnswerId New() => new(Guid.CreateVersion7());
}
```

- [ ] **Step 4: Run — should pass**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/AIHelperNET.Domain/Ids/ tests/AIHelperNET.Domain.Tests/Ids/
git commit -m "feat(domain): add strongly-typed IDs"
```

---

### Task 9: Value objects — AnswerSettings and CodeProfile

**Files:**
- Create: `src/AIHelperNET.Domain/ValueObjects/AnswerSettings.cs`
- Create: `src/AIHelperNET.Domain/ValueObjects/CodeProfile.cs`

No dedicated unit test — these are pure records; equality is tested implicitly in Session tests.

- [ ] **Step 1: Create AnswerSettings**

```csharp
// src/AIHelperNET.Domain/ValueObjects/AnswerSettings.cs
namespace AIHelperNET.Domain.ValueObjects;

public enum AnswerLength     { VeryShort, Short, Medium, Detailed, DeepDive }
public enum AnswerComplexity { Simple, Balanced, Advanced, Senior }
public enum AnswerStyle      { Natural, Interview, Technical, StepByStep, CodeFirst, Architecture, Debugging }
public enum AnswerTone       { Calm, Confident, Professional, Friendly }
public enum AnswerFormat     { VerbalOnly, ExplanationPlusCode, CodeOnly, ExplanationPlusNotes }

public sealed record AnswerSettings(
    AnswerLength Length,
    AnswerComplexity Complexity,
    AnswerStyle Style,
    AnswerTone Tone,
    AnswerFormat Format,
    string OutputLanguage)
{
    public static AnswerSettings Default => new(
        AnswerLength.Medium,
        AnswerComplexity.Balanced,
        AnswerStyle.Interview,
        AnswerTone.Confident,
        AnswerFormat.ExplanationPlusCode,
        "English");
}
```

- [ ] **Step 2: Create CodeProfile**

```csharp
// src/AIHelperNET.Domain/ValueObjects/CodeProfile.cs
namespace AIHelperNET.Domain.ValueObjects;

public sealed record CodeProfile(
    string? ProgrammingLanguage,
    string? BackendFramework,
    string? FrontendFramework,
    string? Database,
    string? CloudDevOps,
    string? Messaging,
    string? ArchitectureStyle,
    string? TestingFramework,
    string? CustomNotes)
{
    public static CodeProfile Empty =>
        new(null, null, null, null, null, null, null, null, null);
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/AIHelperNET.Domain/
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Domain/ValueObjects/
git commit -m "feat(domain): add AnswerSettings and CodeProfile value objects"
```

---

### Task 10: Entities — TranscriptItem, DetectedQuestion, GeneratedAnswer

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/TranscriptItem.cs`
- Create: `src/AIHelperNET.Domain/Sessions/DetectedQuestion.cs`
- Create: `src/AIHelperNET.Domain/Sessions/GeneratedAnswer.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Sessions/EntityTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Domain.Tests/Sessions/EntityTests.cs
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class EntityTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void TranscriptItem_Create_TrimsAndStores()
    {
        var item = TranscriptItem.Create(Speaker.Other, "  Hello  ", Now, 0.9f);
        item.Text.Should().Be("Hello");
        item.Speaker.Should().Be(Speaker.Other);
        item.Confidence.Should().Be(0.9f);
    }

    [Fact]
    public void TranscriptItem_Create_EmptyText_Throws()
    {
        var act = () => TranscriptItem.Create(Speaker.Me, "   ", Now, 0.5f);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void DetectedQuestion_Create_TrimsAndStores()
    {
        var q = DetectedQuestion.Create("  What is CQRS?  ", QuestionSource.Audio, Now);
        q.Text.Should().Be("What is CQRS?");
        q.Source.Should().Be(QuestionSource.Audio);
    }

    [Fact]
    public void GeneratedAnswer_AppendChunk_BuildsContent()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.AppendChunk("Hello ");
        a.AppendChunk("world");
        a.Content.Should().Be("Hello world");
        a.Status.Should().Be(AnswerStatus.Streaming);
    }

    [Fact]
    public void GeneratedAnswer_Complete_SetsStatus()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.Complete(Now.AddSeconds(5));
        a.Status.Should().Be(AnswerStatus.Completed);
        a.CompletedAt.Should().Be(Now.AddSeconds(5));
    }

    [Fact]
    public void GeneratedAnswer_Cancel_SetsStatus()
    {
        var a = GeneratedAnswer.Create(new Domain.Ids.QuestionId(Guid.NewGuid()), Now);
        a.Cancel(Now.AddSeconds(1));
        a.Status.Should().Be(AnswerStatus.Cancelled);
    }
}
```

- [ ] **Step 2: Run — compile error expected**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Implement TranscriptItem**

```csharp
// src/AIHelperNET.Domain/Sessions/TranscriptItem.cs
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed class TranscriptItem
{
    public TranscriptItemId Id { get; }
    public Speaker Speaker { get; }
    public string Text { get; }
    public DateTimeOffset Timestamp { get; }
    public float Confidence { get; }

    private TranscriptItem(TranscriptItemId id, Speaker speaker, string text,
        DateTimeOffset ts, float confidence)
        => (Id, Speaker, Text, Timestamp, Confidence) = (id, speaker, text, ts, confidence);

    public static TranscriptItem Create(Speaker speaker, string text, DateTimeOffset ts, float confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TranscriptItem(TranscriptItemId.New(), speaker, text.Trim(), ts, confidence);
    }

    private TranscriptItem() { } // EF Core
}
```

- [ ] **Step 4: Implement DetectedQuestion**

```csharp
// src/AIHelperNET.Domain/Sessions/DetectedQuestion.cs
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed class DetectedQuestion
{
    public QuestionId Id { get; }
    public string Text { get; }
    public QuestionSource Source { get; }
    public DateTimeOffset DetectedAt { get; }

    private DetectedQuestion(QuestionId id, string text, QuestionSource src, DateTimeOffset at)
        => (Id, Text, Source, DetectedAt) = (id, text, src, at);

    public static DetectedQuestion Create(string text, QuestionSource source, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new DetectedQuestion(QuestionId.New(), text.Trim(), source, at);
    }

    private DetectedQuestion() { } // EF Core
}
```

- [ ] **Step 5: Implement GeneratedAnswer**

```csharp
// src/AIHelperNET.Domain/Sessions/GeneratedAnswer.cs
using System.Text;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed class GeneratedAnswer
{
    private readonly StringBuilder _buffer = new();

    public AnswerId Id { get; }
    public QuestionId QuestionId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public AnswerStatus Status { get; private set; }
    public string Content => _buffer.ToString();

    private GeneratedAnswer(AnswerId id, QuestionId questionId, DateTimeOffset at)
        => (Id, QuestionId, StartedAt, Status) = (id, questionId, at, AnswerStatus.Streaming);

    public static GeneratedAnswer Create(QuestionId questionId, DateTimeOffset at)
        => new(AnswerId.New(), questionId, at);

    public void AppendChunk(string chunk) => _buffer.Append(chunk);

    public void Complete(DateTimeOffset at) { Status = AnswerStatus.Completed; CompletedAt = at; }
    public void Cancel(DateTimeOffset at) { if (Status == AnswerStatus.Streaming) Status = AnswerStatus.Cancelled; CompletedAt = at; }
    public void Fail(DateTimeOffset at) { Status = AnswerStatus.Failed; CompletedAt = at; }

    private GeneratedAnswer() { } // EF Core
}
```

- [ ] **Step 6: Run — should pass**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add src/AIHelperNET.Domain/Sessions/ tests/AIHelperNET.Domain.Tests/Sessions/EntityTests.cs
git commit -m "feat(domain): add TranscriptItem, DetectedQuestion, GeneratedAnswer entities"
```

---

### Task 11: Session aggregate

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/Session.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class SessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_ValidInputs_ReturnsActiveSession()
    {
        var result = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(SessionState.Active);
        result.Value.StartedAt.Should().Be(Now);
    }

    [Fact]
    public void Create_NullSettings_Fails()
    {
        var result = Session.Create(null!, CodeProfile.Empty, Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Create_NullProfile_Fails()
    {
        var result = Session.Create(AnswerSettings.Default, null!, Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddTranscriptItem_ActiveSession_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var item = TranscriptItem.Create(Speaker.Other, "Hello", Now, 0.9f);
        session.AddTranscriptItem(item).IsSuccess.Should().BeTrue();
        session.Transcript.Should().HaveCount(1);
    }

    [Fact]
    public void AddTranscriptItem_StoppedSession_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        var item = TranscriptItem.Create(Speaker.Me, "Late", Now, 0.8f);
        session.AddTranscriptItem(item).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddDetectedQuestion_StoppedSession_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        var q = DetectedQuestion.Create("Why?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void StartAnswer_UnknownQuestion_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var result = session.StartAnswer(QuestionId.New(), Now);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void StartAnswer_KnownQuestion_ReturnsStreamingAnswer()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var result = session.StartAnswer(q.Id, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(AnswerStatus.Streaming);
    }

    [Fact]
    public void Stop_AlreadyStopped_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        session.Stop(Now.AddSeconds(1)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void Stop_Active_SetsEndedAt()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now.AddMinutes(30));
        session.EndedAt.Should().Be(Now.AddMinutes(30));
        session.State.Should().Be(SessionState.Stopped);
    }

    [Fact]
    public void UpdateAnswerSettings_Succeeds()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        var newSettings = AnswerSettings.Default with { OutputLanguage = "Polish" };
        session.UpdateAnswerSettings(newSettings).IsSuccess.Should().BeTrue();
        session.AnswerSettings.OutputLanguage.Should().Be("Polish");
    }
}
```

- [ ] **Step 2: Run — compile error**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Implement Session**

```csharp
// src/AIHelperNET.Domain/Sessions/Session.cs
using AIHelperNET.Domain.Common;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Domain.Sessions;

public sealed class Session
{
    private readonly List<TranscriptItem> _transcript = [];
    private readonly List<DetectedQuestion> _questions = [];
    private readonly List<GeneratedAnswer> _answers = [];

    public SessionId Id { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? EndedAt { get; private set; }
    public SessionState State { get; private set; }
    public AnswerSettings AnswerSettings { get; private set; } = null!;
    public CodeProfile CodeProfile { get; private set; } = null!;

    public IReadOnlyList<TranscriptItem> Transcript => _transcript;
    public IReadOnlyList<DetectedQuestion> Questions => _questions;
    public IReadOnlyList<GeneratedAnswer> Answers => _answers;

    private Session(SessionId id, DateTimeOffset startedAt,
        AnswerSettings answerSettings, CodeProfile codeProfile)
    {
        Id = id;
        StartedAt = startedAt;
        State = SessionState.Active;
        AnswerSettings = answerSettings;
        CodeProfile = codeProfile;
    }

    public static DomainResult<Session> Create(
        AnswerSettings answerSettings, CodeProfile codeProfile, DateTimeOffset now)
    {
        if (answerSettings is null) return DomainResult.Fail<Session>("Answer settings required.");
        if (codeProfile is null) return DomainResult.Fail<Session>("Code profile required.");
        return DomainResult.Ok(new Session(SessionId.New(), now, answerSettings, codeProfile));
    }

    public DomainResult AddTranscriptItem(TranscriptItem item)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot add transcript to a stopped session.");
        _transcript.Add(item);
        return DomainResult.Ok();
    }

    public DomainResult AddDetectedQuestion(DetectedQuestion question)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot add question to a stopped session.");
        _questions.Add(question);
        return DomainResult.Ok();
    }

    public DomainResult<GeneratedAnswer> StartAnswer(QuestionId questionId, DateTimeOffset now)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail<GeneratedAnswer>("Session is not active.");
        if (_questions.All(q => q.Id != questionId))
            return DomainResult.Fail<GeneratedAnswer>("Unknown question.");

        var answer = GeneratedAnswer.Create(questionId, now);
        _answers.Add(answer);
        return DomainResult.Ok(answer);
    }

    public DomainResult UpdateAnswerSettings(AnswerSettings settings)
    {
        AnswerSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        return DomainResult.Ok();
    }

    public DomainResult UpdateCodeProfile(CodeProfile profile)
    {
        CodeProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        return DomainResult.Ok();
    }

    public DomainResult Stop(DateTimeOffset now)
    {
        if (State == SessionState.Stopped)
            return DomainResult.Fail("Session already stopped.");
        State = SessionState.Stopped;
        EndedAt = now;
        return DomainResult.Ok();
    }

    private Session() { } // EF Core
}
```

- [ ] **Step 4: Run — should pass**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/AIHelperNET.Domain/Sessions/Session.cs tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs
git commit -m "feat(domain): add Session aggregate root"
```

---

### Task 12: QuestionDetector pure domain service (TDD)

**Files:**
- Create: `src/AIHelperNET.Domain/Questions/QuestionDetectionResult.cs`
- Create: `src/AIHelperNET.Domain/Questions/QuestionDetector.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Domain.Tests/Questions/QuestionDetectorTests.cs
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Questions;

public class QuestionDetectorTests
{
    private readonly QuestionDetector _sut = new();

    [Theory]
    [InlineData("What is dependency injection?")]
    [InlineData("Explain the SOLID principles")]
    [InlineData("How would you optimize this query")]
    [InlineData("Can you describe CQRS")]
    [InlineData("Implement a binary search")]
    [InlineData("Design a rate limiter")]
    public void Evaluate_QuestionText_DetectsAsQuestion(string text)
    {
        var result = _sut.Evaluate(text, []);
        result.IsQuestion.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
        result.NormalizedText.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("I have ten years of experience.")]
    [InlineData("Let me share my background.")]
    [InlineData("Sure, I can do that.")]
    public void Evaluate_Statement_NotAQuestion(string text)
    {
        var result = _sut.Evaluate(text, []);
        result.IsQuestion.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EmptyText_NotAQuestion()
    {
        _sut.Evaluate("", []).IsQuestion.Should().BeFalse();
        _sut.Evaluate("   ", []).IsQuestion.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NearDuplicate_MarkedDuplicate()
    {
        var recent = new[] { "What is dependency injection in dotnet?" };
        var result = _sut.Evaluate("What is dependency injection in dotnet", recent);
        result.IsDuplicate.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DifferentQuestion_NotDuplicate()
    {
        var recent = new[] { "What is dependency injection?" };
        var result = _sut.Evaluate("How does garbage collection work?", recent);
        result.IsQuestion.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public void Jaccard_IdenticalSets_ReturnsOne()
    {
        var a = new HashSet<string> { "a", "b", "c" };
        QuestionDetector.Jaccard(a, a).Should().Be(1.0);
    }

    [Fact]
    public void Jaccard_DisjointSets_ReturnsZero()
    {
        QuestionDetector.Jaccard(
            new HashSet<string> { "a" },
            new HashSet<string> { "b" }).Should().Be(0.0);
    }

    [Fact]
    public void Jaccard_BothEmpty_ReturnsOne()
    {
        QuestionDetector.Jaccard(
            new HashSet<string>(),
            new HashSet<string>()).Should().Be(1.0);
    }

    [Fact]
    public void Jaccard_PartialOverlap_CorrectValue()
    {
        // {a,b} ∩ {b,c} = {b}, union = {a,b,c} → 1/3
        QuestionDetector.Jaccard(
            new HashSet<string> { "a", "b" },
            new HashSet<string> { "b", "c" }).Should().BeApproximately(1.0 / 3.0, 0.001);
    }
}
```

- [ ] **Step 2: Run — compile error**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Implement QuestionDetectionResult**

```csharp
// src/AIHelperNET.Domain/Questions/QuestionDetectionResult.cs
namespace AIHelperNET.Domain.Questions;

public sealed record QuestionDetectionResult(bool IsQuestion, bool IsDuplicate, string? NormalizedText)
{
    public static QuestionDetectionResult NotAQuestion() => new(false, false, null);
    public static QuestionDetectionResult Duplicate()    => new(true, true, null);
    public static QuestionDetectionResult NewQuestion(string t) => new(true, false, t);
}
```

- [ ] **Step 4: Implement QuestionDetector**

```csharp
// src/AIHelperNET.Domain/Questions/QuestionDetector.cs
namespace AIHelperNET.Domain.Questions;

public sealed class QuestionDetector
{
    private const double DuplicateThreshold = 0.6;

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

    public QuestionDetectionResult Evaluate(string text, IReadOnlyCollection<string> recentQuestions)
    {
        if (string.IsNullOrWhiteSpace(text))
            return QuestionDetectionResult.NotAQuestion();

        var normalized = text.Trim();
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
        if (text.EndsWith('?')) return true;
        var first = FirstWord(text);
        return Interrogatives.Contains(first) || ImperativeVerbs.Contains(first);
    }

    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static IReadOnlySet<string> Tokenize(string text) =>
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

- [ ] **Step 5: Run — all pass**

```powershell
dotnet test tests/AIHelperNET.Domain.Tests/ --logger "console;verbosity=minimal"
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/AIHelperNET.Domain/Questions/ tests/AIHelperNET.Domain.Tests/Questions/
git commit -m "feat(domain): add QuestionDetector pure domain service"
```

---

### Task 13: Harden architecture tests (real assertions)

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs`

Replace the placeholder content with real NetArchTest assertions now that Domain and Application assemblies exist.

- [ ] **Step 1: Rewrite ArchitectureTests**

```csharp
// tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs
using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace AIHelperNET.Integration.Tests.Architecture;

public class ArchitectureTests
{
    private static readonly Assembly DomainAssembly      = typeof(AIHelperNET.Domain.Sessions.Session).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(AIHelperNET.Application.DependencyInjection).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOnApplication()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Application")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_ShouldNotDependOnInfrastructure()
    {
        Types.InAssembly(DomainAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_ShouldNotDependOnInfrastructure()
    {
        Types.InAssembly(ApplicationAssembly)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void QuestionDetector_LivesInDomain()
    {
        typeof(AIHelperNET.Domain.Questions.QuestionDetector).Assembly
            .Should().BeSameAs(DomainAssembly);
    }
}
```

Note: `AIHelperNET.Application.DependencyInjection` will be created in Phase 3. If running this test before Phase 3, the compile will fail — that's expected. Run after Phase 3 Task 17.

- [ ] **Step 2: Commit**

```powershell
git add tests/AIHelperNET.Integration.Tests/Architecture/ArchitectureTests.cs
git commit -m "test(arch): harden NetArchTest assertions with real assemblies"
```

---

**Phase 2 complete.** Continue with `2026-06-02-aihelper-phase3-application.md`.
