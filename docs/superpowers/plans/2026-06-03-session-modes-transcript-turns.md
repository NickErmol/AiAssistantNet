# Session Modes, Live Transcript & ConversationTurn Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add session mode selection (Audio/Screen/Both), live two-speaker transcript panel, and clarification-aware ConversationTurn model to the overlay window.

**Architecture:** Domain-first — enums and entities added to Domain, application commands/pipeline service added to Application, AppDbContext updated for persistence, then new ViewModels and MainOverlayWindow replace the old OverlayWindow in the App layer.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm, Mediator.SourceGenerator, EF Core SQLite, FluentResults, xUnit, FluentAssertions

---

## File Map

**Create (Domain):**
- `src/AIHelperNET.Domain/Sessions/SessionMode.cs`
- `src/AIHelperNET.Domain/Sessions/AudioSourceMode.cs`
- `src/AIHelperNET.Domain/Sessions/ConversationTurnStatus.cs`
- `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs`
- `src/AIHelperNET.Domain/Sessions/AnswerVersion.cs`
- `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs`
- `src/AIHelperNET.Domain/Ids/ConversationTurnId.cs`
- `src/AIHelperNET.Domain/Ids/AnswerVersionId.cs`

**Modify (Domain):**
- `src/AIHelperNET.Domain/Sessions/Session.cs` — add Mode, AudioSource, ConversationTurns, ChangeMode(), AddConversationTurn(), ActiveTurn

**Create (Application):**
- `src/AIHelperNET.Application/Abstractions/ITranscriptSink.cs`
- `src/AIHelperNET.Application/Sessions/Commands/ChangeModeCommand.cs`
- `src/AIHelperNET.Application/Sessions/Commands/DismissTurnCommand.cs`
- `src/AIHelperNET.Application/Sessions/Commands/ResolveTurnCommand.cs`
- `src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerCommand.cs`
- `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`

**Modify (Application):**
- `src/AIHelperNET.Application/Abstractions/IAnswerStreamSink.cs` — new signature with ConversationTurnId + AnswerVersionType
- `src/AIHelperNET.Application/Sessions/Commands/StartSessionCommand.cs` — add Mode + AudioSource params
- `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs` — target ConversationTurnId, append AnswerVersion on complete
- `src/AIHelperNET.Application/Sessions/Dtos/SessionDto.cs` — add Mode, AudioSource

**Modify (Infrastructure):**
- `src/AIHelperNET.Infrastructure/Persistence/AppDbContext.cs` — OwnsMany ConversationTurn + AnswerVersion, Session Mode/AudioSource columns

**Create (App):**
- `src/AIHelperNET.App/Streaming/TranscriptSink.cs`
- `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`
- `src/AIHelperNET.App/ViewModels/TranscriptViewModel.cs`
- `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

**Modify (App):**
- `src/AIHelperNET.App/Streaming/AnswerStreamSink.cs` — new IAnswerStreamSink signature
- `src/AIHelperNET.App/DependencyInjection.cs` — register new ViewModels + sinks
- `src/AIHelperNET.App/App.xaml.cs` — launch MainOverlayWindow, rewire hotkeys

**Delete:**
- `src/AIHelperNET.App/Windows/OverlayWindow.xaml`
- `src/AIHelperNET.App/Windows/OverlayWindow.xaml.cs`
- `src/AIHelperNET.App/ViewModels/OverlayViewModel.cs`

**Tests:**
- `tests/AIHelperNET.Domain.Tests/Sessions/ConversationTurnTests.cs` (new)
- `tests/AIHelperNET.Domain.Tests/Sessions/SessionModeTests.cs` (new)
- `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs` (extend)
- `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` (new)

---

### Task 1: SessionMode, AudioSourceMode enums + IDs

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/SessionMode.cs`
- Create: `src/AIHelperNET.Domain/Sessions/AudioSourceMode.cs`
- Create: `src/AIHelperNET.Domain/Ids/ConversationTurnId.cs`
- Create: `src/AIHelperNET.Domain/Ids/AnswerVersionId.cs`
- Modify: `tests/AIHelperNET.Domain.Tests/Ids/IdTests.cs`

- [ ] **Step 1: Write failing tests for new IDs**

Add to `tests/AIHelperNET.Domain.Tests/Ids/IdTests.cs`:
```csharp
[Fact]
public void ConversationTurnId_New_IsUnique()
{
    var a = ConversationTurnId.New();
    var b = ConversationTurnId.New();
    a.Should().NotBe(b);
}

[Fact]
public void AnswerVersionId_New_IsUnique()
{
    var a = AnswerVersionId.New();
    var b = AnswerVersionId.New();
    a.Should().NotBe(b);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: FAIL — `ConversationTurnId` and `AnswerVersionId` not defined.

- [ ] **Step 3: Create `ConversationTurnId.cs`**

```csharp
namespace AIHelperNET.Domain.Ids;

public readonly record struct ConversationTurnId(Guid Value)
{
    public static ConversationTurnId New() => new(Guid.CreateVersion7());
}
```

- [ ] **Step 4: Create `AnswerVersionId.cs`**

```csharp
namespace AIHelperNET.Domain.Ids;

public readonly record struct AnswerVersionId(Guid Value)
{
    public static AnswerVersionId New() => new(Guid.CreateVersion7());
}
```

- [ ] **Step 5: Create `SessionMode.cs`**

```csharp
namespace AIHelperNET.Domain.Sessions;

public enum SessionMode
{
    AudioOnly,
    ScreenOnly,
    AudioAndScreen
}
```

- [ ] **Step 6: Create `AudioSourceMode.cs`**

```csharp
namespace AIHelperNET.Domain.Sessions;

public enum AudioSourceMode
{
    MicrophoneOnly,
    SystemAudioOnly,
    Both
}
```

- [ ] **Step 7: Run tests to verify they pass**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: all pass.

- [ ] **Step 8: Commit**

```bash
git add src/AIHelperNET.Domain/ tests/AIHelperNET.Domain.Tests/
git commit -m "feat(domain): add SessionMode, AudioSourceMode enums and ConversationTurnId, AnswerVersionId"
```

---

### Task 2: ConversationTurnStatus, AnswerVersionType, AnswerVersion

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/ConversationTurnStatus.cs`
- Create: `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs`
- Create: `src/AIHelperNET.Domain/Sessions/AnswerVersion.cs`

- [ ] **Step 1: Create `ConversationTurnStatus.cs`**

```csharp
namespace AIHelperNET.Domain.Sessions;

public enum ConversationTurnStatus
{
    Detected,
    GeneratingPreliminary,
    PreliminaryReady,
    AwaitingClarification,
    ClarificationReceived,
    GeneratingRefined,
    RefinedReady,
    Dismissed,
    Resolved
}
```

- [ ] **Step 2: Create `AnswerVersionType.cs`**

```csharp
namespace AIHelperNET.Domain.Sessions;

public enum AnswerVersionType
{
    Preliminary,
    RefinedAfterClarification,
    UpdatedWithScreen,
    ManuallyRegenerated
}
```

- [ ] **Step 3: Create `AnswerVersion.cs`**

```csharp
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed record AnswerVersion(
    AnswerVersionId Id,
    AnswerVersionType VersionType,
    string Text,
    DateTimeOffset CreatedAt,
    AnswerVersionId? SupersedesId = null)
{
    public static AnswerVersion Create(
        AnswerVersionType type, string text, DateTimeOffset now,
        AnswerVersionId? supersedes = null)
        => new(AnswerVersionId.New(), type, text, now, supersedes);
}
```

- [ ] **Step 4: Build to verify no errors**

```
dotnet build src/AIHelperNET.Domain/ -v quiet
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Domain/
git commit -m "feat(domain): add ConversationTurnStatus, AnswerVersionType enums and AnswerVersion record"
```

---

### Task 3: ConversationTurn entity

**Files:**
- Create: `src/AIHelperNET.Domain/Sessions/ConversationTurn.cs`
- Create: `tests/AIHelperNET.Domain.Tests/Sessions/ConversationTurnTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AIHelperNET.Domain.Tests/Sessions/ConversationTurnTests.cs`:
```csharp
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Sessions;

public class ConversationTurnTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly SessionId SId = new(Guid.NewGuid());
    private static readonly QuestionId QId = QuestionId.New();

    [Fact]
    public void Create_SetsDetectedStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "What is DI?", Now);
        turn.Status.Should().Be(ConversationTurnStatus.Detected);
        turn.InitialQuestionText.Should().Be("What is DI?");
        turn.AnswerVersions.Should().BeEmpty();
    }

    [Fact]
    public void TransitionTo_ValidTransition_Succeeds()
    {
        var turn = ConversationTurn.Create(SId, QId, "What is DI?", Now);
        var result = turn.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
        result.IsSuccess.Should().BeTrue();
        turn.Status.Should().Be(ConversationTurnStatus.GeneratingPreliminary);
    }

    [Fact]
    public void TransitionTo_FromTerminalState_Fails()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Dismiss();
        var result = turn.TransitionTo(ConversationTurnStatus.GeneratingPreliminary);
        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void AddAnswerVersion_AppendsPreliminary()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var version = AnswerVersion.Create(AnswerVersionType.Preliminary, "Answer text", Now);
        turn.AddAnswerVersion(version);
        turn.AnswerVersions.Should().HaveCount(1);
        turn.AnswerVersions[0].VersionType.Should().Be(AnswerVersionType.Preliminary);
    }

    [Fact]
    public void AttachClarificationQuestion_AddsToList()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var itemId = new TranscriptItemId(Guid.NewGuid());
        turn.AttachClarificationQuestion(itemId);
        turn.ClarificationQuestionIds.Should().Contain(itemId);
    }

    [Fact]
    public void AttachClarificationResponse_AddsToList()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        var itemId = new TranscriptItemId(Guid.NewGuid());
        turn.AttachClarificationResponse(itemId);
        turn.ClarificationResponseIds.Should().Contain(itemId);
    }

    [Fact]
    public void Dismiss_SetsStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Dismiss();
        turn.Status.Should().Be(ConversationTurnStatus.Dismissed);
    }

    [Fact]
    public void Resolve_SetsStatus()
    {
        var turn = ConversationTurn.Create(SId, QId, "Q?", Now);
        turn.Resolve();
        turn.Status.Should().Be(ConversationTurnStatus.Resolved);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: FAIL — `ConversationTurn` not defined.

- [ ] **Step 3: Create `ConversationTurn.cs`**

EF Core cannot map `List<struct>` via `OwnsMany` directly. `ConversationTurn` exposes two internal JSON properties that EF Core maps as TEXT columns; the public collections remain the real source of truth in memory.

```csharp
using System.Text.Json;
using AIHelperNET.Domain.Common;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed class ConversationTurn
{
    private static readonly HashSet<ConversationTurnStatus> TerminalStatuses =
        [ConversationTurnStatus.Dismissed, ConversationTurnStatus.Resolved];

    private List<TranscriptItemId> _clarificationQuestionIds = [];
    private List<TranscriptItemId> _clarificationResponseIds = [];
    private readonly List<AnswerVersion> _answerVersions = [];

    // EF Core JSON bridge — maps private lists to TEXT columns
    internal string ClarificationQuestionIdsJson
    {
        get => JsonSerializer.Serialize(_clarificationQuestionIds.Select(id => id.Value));
        set => _clarificationQuestionIds = string.IsNullOrEmpty(value) ? [] :
            JsonSerializer.Deserialize<Guid[]>(value)!.Select(g => new TranscriptItemId(g)).ToList();
    }
    internal string ClarificationResponseIdsJson
    {
        get => JsonSerializer.Serialize(_clarificationResponseIds.Select(id => id.Value));
        set => _clarificationResponseIds = string.IsNullOrEmpty(value) ? [] :
            JsonSerializer.Deserialize<Guid[]>(value)!.Select(g => new TranscriptItemId(g)).ToList();
    }

    public ConversationTurnId Id { get; }
    public SessionId SessionId { get; }
    public QuestionId InitialQuestionId { get; }
    public string InitialQuestionText { get; }
    public ConversationTurnStatus Status { get; private set; }
    public IReadOnlyList<TranscriptItemId> ClarificationQuestionIds => _clarificationQuestionIds;
    public IReadOnlyList<TranscriptItemId> ClarificationResponseIds => _clarificationResponseIds;
    public IReadOnlyList<AnswerVersion> AnswerVersions => _answerVersions;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private ConversationTurn(ConversationTurnId id, SessionId sessionId,
        QuestionId questionId, string questionText, DateTimeOffset now)
    {
        Id = id;
        SessionId = sessionId;
        InitialQuestionId = questionId;
        InitialQuestionText = questionText;
        Status = ConversationTurnStatus.Detected;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static ConversationTurn Create(
        SessionId sessionId, QuestionId questionId, string questionText, DateTimeOffset now)
        => new(ConversationTurnId.New(), sessionId, questionId, questionText, now);

    public DomainResult TransitionTo(ConversationTurnStatus next)
    {
        if (TerminalStatuses.Contains(Status))
            return DomainResult.Fail($"Cannot transition from terminal status {Status}.");
        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
        return DomainResult.Ok();
    }

    public void AddAnswerVersion(AnswerVersion version)
    {
        _answerVersions.Add(version);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AttachClarificationQuestion(TranscriptItemId itemId)
    {
        _clarificationQuestionIds.Add(itemId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AttachClarificationResponse(TranscriptItemId itemId)
    {
        _clarificationResponseIds.Add(itemId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Dismiss() { Status = ConversationTurnStatus.Dismissed; UpdatedAt = DateTimeOffset.UtcNow; }
    public void Resolve() { Status = ConversationTurnStatus.Resolved;  UpdatedAt = DateTimeOffset.UtcNow; }

#pragma warning disable CS8618
    private ConversationTurn() { }
#pragma warning restore CS8618
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Domain/ tests/AIHelperNET.Domain.Tests/
git commit -m "feat(domain): add ConversationTurn entity with state machine"
```

---

### Task 4: Add Mode, AudioSource, and ConversationTurns to Session

**Files:**
- Modify: `src/AIHelperNET.Domain/Sessions/Session.cs`
- Modify: `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs`

- [ ] **Step 1: Write failing tests**

Add to `tests/AIHelperNET.Domain.Tests/Sessions/SessionTests.cs`:
```csharp
[Fact]
public void Create_DefaultMode_IsAudioAndScreen()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    session.Mode.Should().Be(SessionMode.AudioAndScreen);
    session.AudioSource.Should().Be(AudioSourceMode.Both);
}

[Fact]
public void ChangeMode_ActiveSession_Succeeds()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    var result = session.ChangeMode(SessionMode.AudioOnly, AudioSourceMode.MicrophoneOnly);
    result.IsSuccess.Should().BeTrue();
    session.Mode.Should().Be(SessionMode.AudioOnly);
    session.AudioSource.Should().Be(AudioSourceMode.MicrophoneOnly);
}

[Fact]
public void ChangeMode_StoppedSession_Fails()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    session.Stop(Now);
    session.ChangeMode(SessionMode.ScreenOnly, AudioSourceMode.Both).IsFailed.Should().BeTrue();
}

[Fact]
public void AddConversationTurn_ActiveSession_Succeeds()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    var q = DetectedQuestion.Create("Q?", QuestionSource.Audio, Now);
    session.AddDetectedQuestion(q);
    var result = session.AddConversationTurn(q.Id, "Q?", Now);
    result.IsSuccess.Should().BeTrue();
    session.ConversationTurns.Should().HaveCount(1);
}

[Fact]
public void ActiveTurn_NoTurns_ReturnsNull()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    session.ActiveTurn.Should().BeNull();
}

[Fact]
public void ActiveTurn_DismissedTurn_ReturnsNull()
{
    var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
    var q = DetectedQuestion.Create("Q?", QuestionSource.Audio, Now);
    session.AddDetectedQuestion(q);
    var turn = session.AddConversationTurn(q.Id, "Q?", Now).Value;
    turn.Dismiss();
    session.ActiveTurn.Should().BeNull();
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: FAIL — `Session` missing Mode, ChangeMode, ConversationTurns, ActiveTurn.

- [ ] **Step 3: Update `Session.cs`**

Add these fields and properties (insert after the existing `_answers` list and before `Id`):
```csharp
private readonly List<ConversationTurn> _turns = [];

public SessionMode Mode { get; private set; } = SessionMode.AudioAndScreen;
public AudioSourceMode AudioSource { get; private set; } = AudioSourceMode.Both;
public IReadOnlyList<ConversationTurn> ConversationTurns => _turns;
public ConversationTurn? ActiveTurn =>
    _turns.LastOrDefault(t =>
        t.Status is not ConversationTurnStatus.Dismissed
                 and not ConversationTurnStatus.Resolved);
```

Add these methods (before the EF constructor):
```csharp
public DomainResult ChangeMode(SessionMode mode, AudioSourceMode audioSource)
{
    if (State != SessionState.Active)
        return DomainResult.Fail("Cannot change mode on a stopped session.");
    Mode = mode;
    AudioSource = audioSource;
    return DomainResult.Ok();
}

public DomainResult<ConversationTurn> AddConversationTurn(
    QuestionId questionId, string questionText, DateTimeOffset now)
{
    if (State != SessionState.Active)
        return DomainResult.Fail<ConversationTurn>("Cannot add turn to a stopped session.");
    if (_questions.All(q => q.Id != questionId))
        return DomainResult.Fail<ConversationTurn>("Unknown question.");
    var turn = ConversationTurn.Create(Id, questionId, questionText, now);
    _turns.Add(turn);
    return DomainResult.Ok(turn);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test tests/AIHelperNET.Domain.Tests/ -v quiet
```
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Domain/ tests/AIHelperNET.Domain.Tests/
git commit -m "feat(domain): add SessionMode, ConversationTurns, and ActiveTurn to Session"
```

---

### Task 5: Update Application abstractions

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/ITranscriptSink.cs`
- Modify: `src/AIHelperNET.Application/Abstractions/IAnswerStreamSink.cs`

- [ ] **Step 1: Create `ITranscriptSink.cs`**

```csharp
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

public interface ITranscriptSink
{
    void OnTranscriptItem(TranscriptItem item);
}
```

- [ ] **Step 2: Update `IAnswerStreamSink.cs`**

Replace the entire file:
```csharp
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

public interface IAnswerStreamSink
{
    ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct);
    ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct);
    ValueTask OnErrorAsync(ConversationTurnId turnId, string error, CancellationToken ct);
}
```

- [ ] **Step 3: Build to find all consumers that must be updated**

```
dotnet build -v quiet 2>&1 | Select-String "error"
```
Expected: errors in `AnswerStreamSink.cs` and `GenerateAnswerCommand.cs` — these are fixed in subsequent tasks.

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.Application/Abstractions/
git commit -m "feat(application): add ITranscriptSink and update IAnswerStreamSink signature"
```

---

### Task 6: Update StartSessionCommand and GenerateAnswerCommand

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Commands/StartSessionCommand.cs`
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/SessionDto.cs`

- [ ] **Step 1: Update `SessionDto.cs` — add Mode and AudioSource**

Open `src/AIHelperNET.Application/Sessions/Dtos/SessionDto.cs` and add `Mode` and `AudioSource` properties. The existing file maps Session → SessionDto. Add:
```csharp
public SessionMode Mode { get; init; }
public AudioSourceMode AudioSource { get; init; }
```
Also add the using: `using AIHelperNET.Domain.Sessions;`

- [ ] **Step 2: Update `StartSessionCommand.cs`**

Replace with:
```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record StartSessionCommand(
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    SessionMode Mode = SessionMode.AudioAndScreen,
    AudioSourceMode AudioSource = AudioSourceMode.Both) : IRequest<Result<SessionDto>>;

public sealed class StartSessionHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StartSessionCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        StartSessionCommand command, CancellationToken cancellationToken)
    {
        var create = Session.Create(command.AnswerSettings, command.CodeProfile, clock.GetUtcNow());
        if (create.IsFailed) return Result.Fail(create.Error);

        var session = create.Value;
        session.ChangeMode(command.Mode, command.AudioSource);

        await repository.AddAsync(session, cancellationToken);
        var save = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (save.IsFailed) return save;

        return Result.Ok(SessionMapper.ToDto(session));
    }
}
```

- [ ] **Step 3: Update `GenerateAnswerCommand.cs`**

Replace with:
```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

public sealed record GenerateAnswerCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    AnswerVersionType VersionType = AnswerVersionType.Preliminary,
    string? ScreenContext = null) : IRequest<Result>;

public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateAnswerCommand, Result>
{
    public async ValueTask<Result> Handle(GenerateAnswerCommand cmd, CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var question = session.Questions.FirstOrDefault(q => q.Id == turn.InitialQuestionId);
        if (question is null) return Result.Fail("Question not found.");

        var genStatus = cmd.VersionType == AnswerVersionType.Preliminary
            ? ConversationTurnStatus.GeneratingPreliminary
            : ConversationTurnStatus.GeneratingRefined;
        turn.TransitionTo(genStatus);

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return start.ToResult();
        var answer = start.Value;

        var prompt = PromptBuilderService.Build(
            session.CodeProfile, session.AnswerSettings, question, cmd.ScreenContext);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, ct))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(cmd.TurnId, cmd.VersionType, chunk, ct);
            }
            answer.Complete(clock.GetUtcNow());

            var version = AnswerVersion.Create(cmd.VersionType, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);

            var readyStatus = cmd.VersionType == AnswerVersionType.Preliminary
                ? ConversationTurnStatus.PreliminaryReady
                : ConversationTurnStatus.RefinedReady;
            turn.TransitionTo(readyStatus);

            await streamSink.OnCompleteAsync(cmd.TurnId, cmd.VersionType, ct);
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(cmd.TurnId, ex.Message, ct);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Update `SessionMapper.cs` to map Mode and AudioSource**

Replace the `ToDto` method body in `src/AIHelperNET.Application/Sessions/SessionMapper.cs`:
```csharp
public static SessionDto ToDto(Session session) => new(
    session.Id,
    session.StartedAt,
    session.EndedAt,
    session.State,
    session.AnswerSettings,
    session.CodeProfile,
    session.Transcript.Select(t => new TranscriptItemDto(t.Id, t.Speaker, t.Text, t.Timestamp, t.Confidence)).ToList(),
    session.Questions.Select(q => new DetectedQuestionDto(q.Id, q.Text, q.Source, q.DetectedAt)).ToList(),
    session.Answers.Select(a => new GeneratedAnswerDto(a.Id, a.QuestionId, a.StartedAt, a.CompletedAt, a.Status, a.Content)).ToList())
{
    Mode        = session.Mode,
    AudioSource = session.AudioSource
};
```

Also update `SessionDto` constructor or add init-only properties. If `SessionDto` is a record, add `SessionMode Mode` and `AudioSourceMode AudioSource` as init properties.

- [ ] **Step 5: Build to check progress**

```
dotnet build src/AIHelperNET.Application/ -v quiet
```
Expected: Build succeeded (Application layer).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/
git commit -m "feat(application): update StartSessionCommand with Mode/AudioSource, GenerateAnswerCommand with ConversationTurnId and AnswerVersion snapshot"
```

---

### Task 7: New application commands (ChangeMode, Dismiss, Resolve, Regenerate)

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Commands/ChangeModeCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/DismissTurnCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/ResolveTurnCommand.cs`
- Create: `src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerCommand.cs`

- [ ] **Step 1: Create `ChangeModeCommand.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record ChangeModeCommand(
    SessionId SessionId,
    SessionMode Mode,
    AudioSourceMode AudioSource) : IRequest<Result>;

public sealed class ChangeModeHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<ChangeModeCommand, Result>
{
    public async ValueTask<Result> Handle(ChangeModeCommand cmd, CancellationToken ct)
    {
        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();

        var result = get.Value.ChangeMode(cmd.Mode, cmd.AudioSource);
        if (result.IsFailed) return result;

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 2: Create `DismissTurnCommand.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record DismissTurnCommand(
    SessionId SessionId,
    ConversationTurnId TurnId) : IRequest<Result>;

public sealed class DismissTurnHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<DismissTurnCommand, Result>
{
    public async ValueTask<Result> Handle(DismissTurnCommand cmd, CancellationToken ct)
    {
        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();

        var turn = get.Value.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("Turn not found.");

        turn.Dismiss();
        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 3: Create `ResolveTurnCommand.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record ResolveTurnCommand(
    SessionId SessionId,
    ConversationTurnId TurnId) : IRequest<Result>;

public sealed class ResolveTurnHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<ResolveTurnCommand, Result>
{
    public async ValueTask<Result> Handle(ResolveTurnCommand cmd, CancellationToken ct)
    {
        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();

        var turn = get.Value.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("Turn not found.");

        turn.Resolve();
        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 4: Create `RegenerateAnswerCommand.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

public sealed record RegenerateAnswerCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string? ScreenContext = null) : IRequest<Result>;

public sealed class RegenerateAnswerHandler(
    IMediator mediator) : IRequestHandler<RegenerateAnswerCommand, Result>
{
    public async ValueTask<Result> Handle(RegenerateAnswerCommand cmd, CancellationToken ct)
        => await mediator.Send(new GenerateAnswerCommand(
            cmd.SessionId, cmd.TurnId,
            AnswerVersionType.ManuallyRegenerated,
            cmd.ScreenContext), ct);
}
```

- [ ] **Step 5: Build Application layer**

```
dotnet build src/AIHelperNET.Application/ -v quiet
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/
git commit -m "feat(application): add ChangeModeCommand, DismissTurnCommand, ResolveTurnCommand, RegenerateAnswerCommand"
```

---

### Task 8: TranscriptPipelineService

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Create: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`:
```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TranscriptPipelineServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;

    private static TranscriptItem MakeItem(Speaker speaker, string text)
        => TranscriptItem.Create(speaker, text, Now, 0.9f);

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_NoTurnCreated()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        // Set up an active turn in PreliminaryReady status
        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", Now).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task TranscriptSink_CalledForEveryItem()
    {
        var session = MakeSession();
        var mediator = Substitute.For<IMediator>();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var svc = new TranscriptPipelineService(mediator, transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test tests/AIHelperNET.Application.Tests/ -v quiet
```
Expected: FAIL — `TranscriptPipelineService` not defined.

- [ ] **Step 3: Create `TranscriptPipelineService.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Mediator;

namespace AIHelperNET.Application.Sessions;

public sealed class TranscriptPipelineService(
    IMediator mediator,
    ITranscriptSink transcriptSink)
{
    public async Task ProcessAsync(Session session, TranscriptItem item, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        var activeTurn = session.ActiveTurn;

        if (item.Speaker == Speaker.Other)
        {
            var detection = QuestionDetector.Detect(item.Text);
            if (!detection.IsQuestion) return;

            if (activeTurn is null)
            {
                var q = DetectedQuestion.Create(item.Text, QuestionSource.Audio, item.Timestamp);
                session.AddDetectedQuestion(q);
                var turnResult = session.AddConversationTurn(q.Id, item.Text, item.Timestamp);
                if (turnResult.IsFailed) return;
                var turn = turnResult.Value;

                _ = mediator.Send(
                    new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary), ct);
            }
            else if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
            {
                activeTurn.AttachClarificationResponse(item.Id);
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);

                _ = mediator.Send(
                    new GenerateAnswerCommand(
                        session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification), ct);
            }
        }
        else if (item.Speaker == Speaker.Me && activeTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = QuestionDetector.Detect(item.Text);
            if (!detection.IsQuestion) return;

            activeTurn.AttachClarificationQuestion(item.Id);
            activeTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
        }
    }
}
```

- [ ] **Step 4: Add NSubstitute to Application.Tests if not present**

Check `tests/AIHelperNET.Application.Tests/AIHelperNET.Application.Tests.csproj` — if `NSubstitute` is not referenced, add it:
```
dotnet add tests/AIHelperNET.Application.Tests/ package NSubstitute
```

- [ ] **Step 5: Run tests to verify they pass**

```
dotnet test tests/AIHelperNET.Application.Tests/ -v quiet
```
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Application/ tests/AIHelperNET.Application.Tests/
git commit -m "feat(application): add TranscriptPipelineService with turn lifecycle logic"
```

---

### Task 9: Update AppDbContext for ConversationTurn persistence

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Update `AppDbContext.cs`**

Add the following to `OnModelCreating` in `AppDbContext.cs`, after the existing `s.OwnsMany(x => x.Answers, ...)` block:

```csharp
// Session mode columns
s.Property(x => x.Mode).HasConversion<int>();
s.Property(x => x.AudioSource).HasConversion<int>();

s.OwnsMany(x => x.ConversationTurns, ct =>
{
    ct.HasKey(x => x.Id);
    ct.Property(x => x.Id)
        .HasConversion(id => id.Value, v => new ConversationTurnId(v));
    ct.Property(x => x.SessionId)
        .HasConversion(id => id.Value, v => new SessionId(v));
    ct.Property(x => x.InitialQuestionId)
        .HasConversion(id => id.Value, v => new QuestionId(v));
    ct.Property(x => x.InitialQuestionText).HasMaxLength(2000);
    ct.Property(x => x.Status).HasConversion<int>();
    ct.Property(x => x.CreatedAt)
        .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                       ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
    ct.Property(x => x.UpdatedAt)
        .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                       ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));

    ct.Navigation(x => x.ClarificationQuestionIds)
        .UsePropertyAccessMode(PropertyAccessMode.Field);
    ct.Navigation(x => x.ClarificationResponseIds)
        .UsePropertyAccessMode(PropertyAccessMode.Field);
    ct.Navigation(x => x.AnswerVersions)
        .UsePropertyAccessMode(PropertyAccessMode.Field);

    // JSON bridge properties for private List<TranscriptItemId> fields
    ct.Property("ClarificationQuestionIdsJson")
        .HasColumnName("ClarificationQuestionIds")
        .HasColumnType("TEXT")
        .HasDefaultValue("[]");
    ct.Property("ClarificationResponseIdsJson")
        .HasColumnName("ClarificationResponseIds")
        .HasColumnType("TEXT")
        .HasDefaultValue("[]");

    ct.OwnsMany(x => x.AnswerVersions, av =>
    {
        av.HasKey(x => x.Id);
        av.Property(x => x.Id)
            .HasConversion(id => id.Value, v => new AnswerVersionId(v));
        av.Property(x => x.VersionType).HasConversion<int>();
        av.Property(x => x.Text).HasMaxLength(32000);
        av.Property(x => x.SupersedesId)
            .HasConversion(
                id => id.HasValue ? (Guid?)id.Value.Value : null,
                v  => v.HasValue ? new AnswerVersionId(v.Value) : null);
        av.Property(x => x.CreatedAt)
            .HasConversion(dto => dto.ToUnixTimeMilliseconds(),
                           ms  => DateTimeOffset.FromUnixTimeMilliseconds(ms));
    });
});
```

Also add the required usings at the top of `AppDbContext.cs`:
```csharp
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
```

- [ ] **Step 2: Delete old SQLite database to force recreation (dev only)**

```
Remove-Item "$env:LOCALAPPDATA\AIHelperNET\sessions.db" -ErrorAction SilentlyContinue
```

- [ ] **Step 3: Build Infrastructure to verify**

```
dotnet build src/AIHelperNET.Infrastructure/ -v quiet
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run integration tests**

```
dotnet test tests/AIHelperNET.Integration.Tests/ -v quiet
```
Expected: all pass (EnsureCreated recreates the schema).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Infrastructure/
git commit -m "feat(infra): add ConversationTurn, AnswerVersion, SessionMode to AppDbContext"
```

---

### Task 10: Update AnswerStreamSink and add TranscriptSink

**Files:**
- Modify: `src/AIHelperNET.App/Streaming/AnswerStreamSink.cs`
- Create: `src/AIHelperNET.App/Streaming/TranscriptSink.cs`

- [ ] **Step 1: Replace `AnswerStreamSink.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.App.Streaming;

public sealed class AnswerStreamSink : IAnswerStreamSink
{
    private Action<ConversationTurnId, AnswerVersionType, string>? _chunkHandler;
    private Action<ConversationTurnId, AnswerVersionType>? _completeHandler;
    private Action<ConversationTurnId, string>? _errorHandler;

    public void SetHandlers(
        Action<ConversationTurnId, AnswerVersionType, string> onChunk,
        Action<ConversationTurnId, AnswerVersionType> onComplete,
        Action<ConversationTurnId, string> onError)
    {
        _chunkHandler    = onChunk;
        _completeHandler = onComplete;
        _errorHandler    = onError;
    }

    public ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct)
    {
        if (_chunkHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _chunkHandler(turnId, versionType, chunk));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct)
    {
        if (_completeHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _completeHandler(turnId, versionType));
        return ValueTask.CompletedTask;
    }

    public ValueTask OnErrorAsync(ConversationTurnId turnId, string error, CancellationToken ct)
    {
        if (_errorHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _errorHandler(turnId, error));
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Create `TranscriptSink.cs`**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.App.Streaming;

public sealed class TranscriptSink : ITranscriptSink
{
    private Action<TranscriptItem>? _handler;

    public void SetHandler(Action<TranscriptItem> handler) => _handler = handler;

    public void OnTranscriptItem(TranscriptItem item)
    {
        if (_handler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _handler(item));
    }
}
```

- [ ] **Step 3: Build App layer to check**

```
dotnet build src/AIHelperNET.App/ -v quiet 2>&1 | Select-Object -Last 5
```
Expected: errors in `OverlayViewModel.cs` only (references old sink API) — that file is replaced in Task 13.

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/Streaming/
git commit -m "feat(app): update AnswerStreamSink and add TranscriptSink"
```

---

### Task 11: SessionControlViewModel and TranscriptViewModel

**Files:**
- Create: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`
- Create: `src/AIHelperNET.App/ViewModels/TranscriptViewModel.cs`

- [ ] **Step 1: Create `SessionControlViewModel.cs`**

```csharp
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed partial class SessionControlViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private SessionMode _mode = SessionMode.AudioAndScreen;
    [ObservableProperty] private AudioSourceMode _audioSource = AudioSourceMode.Both;
    [ObservableProperty] private bool _isMicActive;
    [ObservableProperty] private bool _isSystemAudioActive;
    [ObservableProperty] private bool _isOcrReady = true;
    [ObservableProperty] private bool _isAiConnected;

    public Domain.Ids.SessionId? ActiveSessionId { get; private set; }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsSessionActive)
        {
            var settingsResult = await mediator.Send(new GetSettingsQuery());
            var settings = settingsResult.IsSuccess ? settingsResult.Value : null;

            var result = await mediator.Send(new StartSessionCommand(
                settings?.AnswerSettings ?? AnswerSettings.Default,
                settings?.CodeProfile   ?? CodeProfile.Empty,
                Mode, AudioSource));

            if (result.IsSuccess)
            {
                ActiveSessionId = result.Value.Id;
                IsSessionActive = true;
                IsMicActive     = AudioSource is AudioSourceMode.MicrophoneOnly or AudioSourceMode.Both;
                IsSystemAudioActive = AudioSource is AudioSourceMode.SystemAudioOnly or AudioSourceMode.Both;
            }
        }
        else if (ActiveSessionId is { } id)
        {
            await mediator.Send(new StopSessionCommand(id));
            IsSessionActive     = false;
            IsMicActive         = false;
            IsSystemAudioActive = false;
            ActiveSessionId     = null;
        }
    }

    [RelayCommand]
    private async Task ChangeModeAsync()
    {
        if (ActiveSessionId is not { } id) return;
        await mediator.Send(new ChangeModeCommand(id, Mode, AudioSource));
        IsMicActive         = AudioSource is AudioSourceMode.MicrophoneOnly or AudioSourceMode.Both;
        IsSystemAudioActive = AudioSource is AudioSourceMode.SystemAudioOnly or AudioSourceMode.Both;
    }

    partial void OnModeChanged(SessionMode value)
    {
        if (IsSessionActive) _ = ChangeModeAsync();
    }

    partial void OnAudioSourceChanged(AudioSourceMode value)
    {
        if (IsSessionActive) _ = ChangeModeAsync();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;
}
```

- [ ] **Step 2: Create `TranscriptViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using AIHelperNET.Domain.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

public sealed class TranscriptItemVm(Speaker speaker, string text, DateTimeOffset timestamp)
{
    public string SpeakerLabel   => speaker == Speaker.Me ? "Me" : "Other";
    public string SpeakerColor   => speaker == Speaker.Me ? "#FFAA44" : "#44AAFF";
    public string Text           => text;
    public string TimestampLabel => timestamp.ToLocalTime().ToString("HH:mm:ss");
}

public sealed partial class TranscriptViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TranscriptItemVm> _items = [];

    public void AddItem(TranscriptItem item)
        => Items.Add(new TranscriptItemVm(item.Speaker, item.Text, item.Timestamp));

    public void Clear() => Items.Clear();
}
```

- [ ] **Step 3: Build App to check**

```
dotnet build src/AIHelperNET.App/ -v quiet 2>&1 | Select-Object -Last 5
```
Expected: still errors in `OverlayViewModel.cs` (deleted in Task 13), new files compile cleanly.

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/
git commit -m "feat(app): add SessionControlViewModel and TranscriptViewModel"
```

---

### Task 12: ConversationTurnViewModel

**Files:**
- Create: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`

- [ ] **Step 1: Create `ConversationTurnViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed class AnswerVersionVm(AnswerVersionId id, AnswerVersionType type, DateTimeOffset createdAt)
{
    public AnswerVersionId Id           => id;
    public string VersionLabel          => type switch
    {
        AnswerVersionType.Preliminary               => "Preliminary",
        AnswerVersionType.RefinedAfterClarification => "Refined — after clarification",
        AnswerVersionType.UpdatedWithScreen         => "Updated with screen",
        AnswerVersionType.ManuallyRegenerated       => "Manually regenerated",
        _                                           => type.ToString()
    };
    public string TimeLabel             => createdAt.ToLocalTime().ToString("HH:mm:ss");
    public string Text                  { get; set; } = string.Empty;
    public bool IsLatest                { get; set; }
}

public sealed class TurnVm(ConversationTurnId id, string initialQuestion)
{
    public ConversationTurnId Id                              => id;
    public string InitialQuestion                             => initialQuestion;
    public ConversationTurnStatus Status                      { get; set; } = ConversationTurnStatus.Detected;
    public string StatusLabel                                 => Status.ToString();
    public ObservableCollection<AnswerVersionVm> AnswerVersions { get; } = [];
    public AnswerVersionVm? LatestVersion                     => AnswerVersions.FirstOrDefault(v => v.IsLatest);
}

public sealed partial class ConversationTurnViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TurnVm> _turns = [];
    [ObservableProperty] private SessionId? _activeSessionId;

    public TurnVm? GetTurn(ConversationTurnId id)
        => Turns.FirstOrDefault(t => t.Id == id);

    public void OnChunk(ConversationTurnId turnId, AnswerVersionType versionType, string chunk)
    {
        var turn = Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn is null) return;

        var version = turn.AnswerVersions.FirstOrDefault(v => v.IsLatest && !IsComplete(v));
        if (version is null)
        {
            foreach (var v in turn.AnswerVersions) v.IsLatest = false;
            version = new AnswerVersionVm(AnswerVersionId.New(), versionType, DateTimeOffset.UtcNow)
                { IsLatest = true };
            turn.AnswerVersions.Insert(0, version);
        }
        version.Text += chunk;
    }

    public void OnComplete(ConversationTurnId turnId, AnswerVersionType versionType)
    {
        // Version is already marked IsLatest — no further action needed for streaming end.
    }

    public void OnError(ConversationTurnId turnId, string error)
    {
        var turn = Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn is null) return;
        var errVersion = new AnswerVersionVm(AnswerVersionId.New(), AnswerVersionType.Preliminary,
            DateTimeOffset.UtcNow) { Text = $"[Error: {error}]", IsLatest = true };
        turn.AnswerVersions.Insert(0, errVersion);
    }

    public void AddTurn(ConversationTurnId id, string question)
        => Turns.Insert(0, new TurnVm(id, question));

    private static bool IsComplete(AnswerVersionVm v) => false; // streaming appends until OnComplete

    [RelayCommand]
    private async Task RegenerateAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new RegenerateAnswerCommand(sid, turn.Id));
    }

    [RelayCommand]
    private async Task DismissAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new DismissTurnCommand(sid, turn.Id));
        Turns.Remove(turn);
    }

    [RelayCommand]
    private async Task ResolveAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new ResolveTurnCommand(sid, turn.Id));
        Turns.Remove(turn);
    }

    [RelayCommand]
    private void CopyLatest(TurnVm? turn)
    {
        var text = turn?.LatestVersion?.Text;
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
    }

    [RelayCommand]
    private async Task CaptureScreenAsync()
    {
        if (ActiveSessionId is not { } sid) return;
        var result = await mediator.Send(new CaptureScreenCommand());
        if (result.IsSuccess && Turns.FirstOrDefault() is { } activeTurn)
        {
            await mediator.Send(new RegenerateAnswerCommand(sid, activeTurn.Id, result.Value));
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```
dotnet build src/AIHelperNET.App/ -v quiet 2>&1 | Select-Object -Last 5
```

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat(app): add ConversationTurnViewModel with streaming, dismiss, resolve, regenerate"
```

---

### Task 13: MainOverlayWindow XAML and code-behind

**Files:**
- Create: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`
- Create: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

- [ ] **Step 1: Create `MainOverlayWindow.xaml`**

```xml
<Window x:Class="AIHelperNET.App.Windows.MainOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:AIHelperNET.App.ViewModels"
        Title="AIHelper"
        Width="600" Height="500"
        MinWidth="320" MinHeight="260"
        WindowStyle="None"
        Background="#1A1A2E"
        Opacity="0.75"
        Topmost="True"
        ResizeMode="CanResizeWithGrip"
        ShowInTaskbar="False">
    <Window.Resources>
        <Style x:Key="RadioBtn" TargetType="RadioButton">
            <Setter Property="Foreground" Value="#AAD4D4D4"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="Margin" Value="0,2"/>
        </Style>
        <Style x:Key="StatusDot" TargetType="Ellipse">
            <Setter Property="Width" Value="7"/>
            <Setter Property="Height" Value="7"/>
            <Setter Property="Margin" Value="0,0,4,0"/>
            <Setter Property="Fill" Value="#FF444444"/>
        </Style>
    </Window.Resources>

    <DockPanel>
        <!-- Title bar -->
        <Border DockPanel.Dock="Top" Background="#0D0D1A" Padding="8,4"
                MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <!-- Sidebar toggle (shown when sidebar hidden) -->
                <Button Grid.Column="0" x:Name="ShowSidebarBtn" Content="▶"
                        Visibility="{Binding SessionControl.IsSidebarVisible,
                            Converter={StaticResource InverseBoolToVisibilityConverter}}"
                        Command="{Binding SessionControl.ToggleSidebarCommand}"
                        Background="Transparent" Foreground="#666" BorderThickness="0"
                        FontSize="9" Margin="0,0,6,0"/>

                <TextBlock Grid.Column="1" Foreground="#88D4D4D4" FontSize="10" VerticalAlignment="Center">
                    <Run Text="AIHelper"/>
                    <Run Text=" · "/>
                    <Run Text="●" Foreground="{Binding SessionControl.IsSessionActive,
                        Converter={StaticResource BoolToColorConverter},
                        ConverterParameter='#44FF88|#666666'}"/>
                    <Run Text="{Binding SessionControl.IsSessionActive,
                        Converter={StaticResource BoolToStringConverter},
                        ConverterParameter='Listening|Stopped'}"/>
                </TextBlock>

                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Content="{Binding SessionControl.IsSessionActive,
                        Converter={StaticResource BoolToStringConverter},
                        ConverterParameter='Stop|Start'}"
                            Command="{Binding SessionControl.ToggleSessionCommand}"
                            Background="#2A2A4A" Foreground="#CCC" BorderThickness="0"
                            FontSize="9" Padding="6,2" Margin="0,0,4,0"/>
                    <Button Content="⚙" Background="Transparent" Foreground="#666"
                            BorderThickness="0" FontSize="11"
                            Click="OpenSettings_Click"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Sidebar -->
        <Border x:Name="SidebarBorder" DockPanel.Dock="Left"
                Width="{Binding SessionControl.IsSidebarVisible,
                    Converter={StaticResource BoolToWidthConverter},
                    ConverterParameter='120'}"
                Background="#0A0A1A" BorderBrush="#333" BorderThickness="0,0,1,0"
                Padding="8">
            <StackPanel>
                <TextBlock Text="MODE" Foreground="#555" FontSize="8" Margin="0,0,0,4"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Audio Only"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=AudioOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Screen Only"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=ScreenOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Audio + Screen"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=AudioAndScreen}"/>

                <TextBlock Text="AUDIO SOURCE" Foreground="#555" FontSize="8" Margin="0,10,0,4"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Mic Only"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=MicrophoneOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="System Only"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=SystemAudioOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Both"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=Both}"/>

                <TextBlock Text="STATUS" Foreground="#555" FontSize="8" Margin="0,10,0,4"/>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsMicActive,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="Mic" Foreground="#888" FontSize="9"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsSystemAudioActive,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="System" Foreground="#888" FontSize="9"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsOcrReady,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="OCR" Foreground="#888" FontSize="9"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsAiConnected,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="AI" Foreground="#888" FontSize="9"/>
                </StackPanel>

                <Button Content="◀ Hide" HorizontalAlignment="Left"
                        Command="{Binding SessionControl.ToggleSidebarCommand}"
                        Background="Transparent" Foreground="#444"
                        BorderThickness="0" FontSize="9" Margin="0,12,0,0"/>
            </StackPanel>
        </Border>

        <!-- Main content: transcript + splitter + answer -->
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="*" MinHeight="60"/>
                <RowDefinition Height="4"/>
                <RowDefinition Height="*" MinHeight="80"/>
            </Grid.RowDefinitions>

            <!-- Transcript panel -->
            <Border Grid.Row="0" Background="#0D0D1A" Margin="0,0,0,0">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top" Text="TRANSCRIPT"
                               Foreground="#555" FontSize="8" Margin="8,6,8,3"/>
                    <ScrollViewer x:Name="TranscriptScroll"
                                  VerticalScrollBarVisibility="Auto"
                                  HorizontalScrollBarVisibility="Disabled"
                                  Margin="8,0,8,6">
                        <ItemsControl ItemsSource="{Binding Transcript.Items}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal" Margin="0,2">
                                        <TextBlock Text="{Binding TimestampLabel}"
                                                   Foreground="#444" FontSize="9" Margin="0,0,6,0"/>
                                        <TextBlock Text="{Binding SpeakerLabel}"
                                                   Foreground="{Binding SpeakerColor}"
                                                   FontSize="9" FontWeight="SemiBold" Margin="0,0,4,0"/>
                                        <TextBlock Text="{Binding Text}" Foreground="#CCC"
                                                   FontSize="9" TextWrapping="Wrap"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </DockPanel>
            </Border>

            <!-- Splitter -->
            <GridSplitter Grid.Row="1" HorizontalAlignment="Stretch"
                          Background="#2A2A4A" Height="4"/>

            <!-- Answer panel -->
            <Border Grid.Row="2" Background="#0D0D1A">
                <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="8">
                    <ItemsControl ItemsSource="{Binding ConversationTurn.Turns}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Margin="0,0,0,8" Padding="6"
                                        Background="#111827" CornerRadius="4">
                                    <StackPanel>
                                        <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                            <TextBlock Text="❓ " Foreground="#FFCC44" FontSize="9"/>
                                            <TextBlock Text="{Binding InitialQuestion}"
                                                       Foreground="#FFCC44" FontSize="9"
                                                       FontWeight="SemiBold" TextWrapping="Wrap"/>
                                        </StackPanel>
                                        <TextBlock Text="{Binding StatusLabel}"
                                                   Foreground="#555" FontSize="8" Margin="0,0,0,4"/>

                                        <!-- Latest answer -->
                                        <TextBlock Text="{Binding LatestVersion.Text}"
                                                   Foreground="#EEE" FontSize="11"
                                                   TextWrapping="Wrap" FontFamily="Cascadia Mono, Consolas"
                                                   Margin="0,0,0,4"/>

                                        <!-- Actions -->
                                        <StackPanel Orientation="Horizontal">
                                            <Button Content="Copy"
                                                    Command="{Binding DataContext.ConversationTurn.CopyLatestCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}"
                                                    Style="{StaticResource ActionBtn}"/>
                                            <Button Content="Regen"
                                                    Command="{Binding DataContext.ConversationTurn.RegenerateCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}"
                                                    Style="{StaticResource ActionBtn}"/>
                                            <Button Content="Dismiss"
                                                    Command="{Binding DataContext.ConversationTurn.DismissCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}"
                                                    Style="{StaticResource ActionBtn}"/>
                                            <Button Content="Resolve"
                                                    Command="{Binding DataContext.ConversationTurn.ResolveCommand,
                                                        RelativeSource={RelativeSource AncestorType=Window}}"
                                                    CommandParameter="{Binding}"
                                                    Style="{StaticResource ActionBtn}"/>
                                        </StackPanel>
                                    </StackPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Create `MainOverlayWindow.xaml.cs`**

```csharp
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using Serilog;

namespace AIHelperNET.App.Windows;

public sealed class MainOverlayWindowContext(
    SessionControlViewModel sessionControl,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn)
{
    public SessionControlViewModel SessionControl  => sessionControl;
    public TranscriptViewModel Transcript          => transcript;
    public ConversationTurnViewModel ConversationTurn => conversationTurn;
}

public partial class MainOverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_MONITOR            = 0x00000001;

    public MainOverlayWindow(MainOverlayWindowContext context, SettingsWindow settingsWindow)
    {
        InitializeComponent();
        DataContext = context;
        _settingsWindow = settingsWindow;
    }

    private readonly SettingsWindow _settingsWindow;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            if (!SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                Log.Warning("SetWindowDisplayAffinity failed — overlay may be visible to screen capture");
            else
                Log.Information("Overlay: WDA_MONITOR applied");
        }
        else
        {
            Log.Information("Overlay: WDA_EXCLUDEFROMCAPTURE applied");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
```

- [ ] **Step 3: Add converters to App.xaml resources**

In `src/AIHelperNET.App/App.xaml`, add the following inside `<Application.Resources>`:
```xml
<Application.Resources>
    <ResourceDictionary>
        <local:BoolToVisibilityInverseConverter x:Key="InverseBoolToVisibilityConverter"/>
        <local:BoolToColorConverter x:Key="BoolToColorConverter"/>
        <local:BoolToStringConverter x:Key="BoolToStringConverter"/>
        <local:BoolToWidthConverter x:Key="BoolToWidthConverter"/>
        <local:EnumToBoolConverter x:Key="EnumToBoolConverter"/>
        <Style x:Key="ActionBtn" TargetType="Button">
            <Setter Property="Background" Value="#2A2A4A"/>
            <Setter Property="Foreground" Value="#AAA"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FontSize" Value="9"/>
            <Setter Property="Padding" Value="6,2"/>
            <Setter Property="Margin" Value="0,0,4,0"/>
        </Style>
    </ResourceDictionary>
</Application.Resources>
```

- [ ] **Step 4: Create `Converters.cs` in the App project**

Create `src/AIHelperNET.App/Converters.cs`:
```csharp
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace AIHelperNET.App;

public sealed class BoolToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class BoolToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var parts = (p as string)?.Split('|') ?? ["#FFFFFF", "#444444"];
        var hex = value is true ? parts[0] : parts[1];
        var color = (Color)ColorConverter.ConvertFromString(hex);
        return new SolidColorBrush(color);
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class BoolToStringConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        var parts = (p as string)?.Split('|') ?? ["True", "False"];
        return value is true ? parts[0] : parts[1];
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class BoolToWidthConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? double.Parse(p as string ?? "120") : 0.0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotSupportedException();
}

public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value?.ToString() == p?.ToString();
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
    {
        if (value is not true) return Binding.DoNothing;
        return Enum.Parse(t, p?.ToString() ?? string.Empty);
    }
}
```

- [ ] **Step 5: Build to check**

```
dotnet build src/AIHelperNET.App/ -v quiet 2>&1 | Select-Object -Last 5
```

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.App/Windows/ src/AIHelperNET.App/Converters.cs
git commit -m "feat(app): add MainOverlayWindow with transcript, answer panels, and sidebar"
```

---

### Task 14: Wire up DI, App.xaml.cs, hotkeys — delete OverlayWindow

**Files:**
- Modify: `src/AIHelperNET.App/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs`
- Delete: `src/AIHelperNET.App/Windows/OverlayWindow.xaml`
- Delete: `src/AIHelperNET.App/Windows/OverlayWindow.xaml.cs`
- Delete: `src/AIHelperNET.App/ViewModels/OverlayViewModel.cs`

- [ ] **Step 1: Update `DependencyInjection.cs`**

Replace the contents of `src/AIHelperNET.App/DependencyInjection.cs`:
```csharp
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.App;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // Sinks (singleton — shared between infrastructure and ViewModels)
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());
        services.AddSingleton<TranscriptSink>();
        services.AddSingleton<ITranscriptSink>(sp => sp.GetRequiredService<TranscriptSink>());

        // ViewModels
        services.AddSingleton<SessionControlViewModel>();
        services.AddSingleton<TranscriptViewModel>();
        services.AddSingleton<ConversationTurnViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Window context + windows
        services.AddSingleton<MainOverlayWindowContext>();
        services.AddSingleton<MainOverlayWindow>();
        services.AddSingleton<SettingsWindow>();

        return services;
    }
}
```

- [ ] **Step 2: Update `App.xaml.cs`**

Replace with:
```csharp
using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AIHelperNET.App;

public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureAIHelper();
        _host = builder.Build();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();

        var overlay = _host.Services.GetRequiredService<MainOverlayWindow>();

        // Wire TranscriptSink → TranscriptViewModel
        var transcriptSink = _host.Services.GetRequiredService<TranscriptSink>();
        var transcriptVm   = _host.Services.GetRequiredService<TranscriptViewModel>();
        transcriptSink.SetHandler(item => transcriptVm.AddItem(item));

        // Wire AnswerStreamSink → ConversationTurnViewModel
        var answerSink     = _host.Services.GetRequiredService<AnswerStreamSink>();
        var turnVm         = _host.Services.GetRequiredService<ConversationTurnViewModel>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => turnVm.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));

        overlay.Show();
        WireHotkeys(overlay);
    }

    private void WireHotkeys(MainOverlayWindow overlay)
    {
        var hotkeys   = _host.Services.GetRequiredService<IGlobalHotkeyService>() as GlobalHotkeyService;
        if (hotkeys is null) return;

        var hwnd      = new WindowInteropHelper(overlay).Handle;
        hotkeys.Initialize(hwnd);

        hotkeys.Register(HotkeyId.ToggleSession,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Space);
        hotkeys.Register(HotkeyId.CaptureScreen,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.S);
        hotkeys.Register(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q);
        hotkeys.Register(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.C);
        hotkeys.Register(HotkeyId.ToggleOverlay,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.H);

        var sessionVm = _host.Services.GetRequiredService<SessionControlViewModel>();
        var turnVm    = _host.Services.GetRequiredService<ConversationTurnViewModel>();

        hotkeys.HotkeyPressed += (_, id) =>
        {
            switch (id)
            {
                case HotkeyId.ToggleSession:  _ = sessionVm.ToggleSessionCommand.ExecuteAsync(null); break;
                case HotkeyId.CaptureScreen:  _ = turnVm.CaptureScreenCommand.ExecuteAsync(null);   break;
                case HotkeyId.GenerateAnswer: _ = turnVm.RegenerateCommand.ExecuteAsync(
                    turnVm.Turns.FirstOrDefault()); break;
                case HotkeyId.CopyAnswer:     turnVm.CopyLatestCommand.Execute(
                    turnVm.Turns.FirstOrDefault()); break;
                case HotkeyId.ToggleOverlay:  sessionVm.ToggleSidebarCommand.Execute(null); break;
            }
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

- [ ] **Step 3: Delete old files**

```
Remove-Item "src\AIHelperNET.App\Windows\OverlayWindow.xaml" -Force
Remove-Item "src\AIHelperNET.App\Windows\OverlayWindow.xaml.cs" -Force
Remove-Item "src\AIHelperNET.App\ViewModels\OverlayViewModel.cs" -Force
```

- [ ] **Step 4: Build entire solution**

```
dotnet build -v quiet 2>&1 | Select-Object -Last 10
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run all tests**

```
dotnet test -v quiet
```
Expected: all existing tests pass.

- [ ] **Step 6: Delete old SQLite db and launch app**

```
Remove-Item "$env:LOCALAPPDATA\AIHelperNET\sessions.db" -ErrorAction SilentlyContinue
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj --no-build
```
Check log: `$env:LOCALAPPDATA\AIHelperNET\logs\log-<today>.txt` — must show:
- `Application started`
- `Overlay: WDA_EXCLUDEFROMCAPTURE applied`

- [ ] **Step 7: Commit**

```bash
git add src/AIHelperNET.App/ --all
git commit -m "feat(app): wire MainOverlayWindow, delete OverlayWindow, rewire hotkeys"
```

---

### Task 15: Final smoke test and push

- [ ] **Step 1: Run all tests**

```
dotnet test -v quiet
```
Expected: all pass (Domain ≥40, Application ≥16, Integration ≥7).

- [ ] **Step 2: Build release**

```
dotnet build -c Release -v quiet 2>&1 | Select-Object -Last 5
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Push develop**

```bash
git push origin develop
```

- [ ] **Step 4: Verify Definition of Done**

Manually verify in the running app:
- [ ] Window launches, log shows `WDA_EXCLUDEFROMCAPTURE applied`
- [ ] Sidebar collapses/expands via ◀ Hide / ▶ Show
- [ ] Mode radio buttons change `SessionMode` in sidebar
- [ ] Audio source radio buttons change `AudioSourceMode`
- [ ] Start / Stop session hotkey (`Ctrl+Shift+Space`) toggles session active state
- [ ] Status dots update when session starts
