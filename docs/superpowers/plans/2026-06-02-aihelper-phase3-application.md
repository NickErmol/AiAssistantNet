# AIHelperNET — Phase 3: Application Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 2 complete (Domain layer green).

**Goal:** Implement the Application layer — all ports, DTOs, CQRS commands/queries with handlers, pipeline behaviors, PromptBuilderService, Mapperly mapper, and DI registration.

**Architecture:** Mediator.SourceGenerator for zero-alloc CQRS dispatch. Handlers return `FluentResults.Result<T>`. Pipeline behaviors for logging and validation. No infrastructure references.

**Tech Stack:** Mediator.SourceGenerator 3.x, FluentResults 3.x, FluentValidation 12.x, Riok.Mapperly 4.x, NSubstitute 5.x (tests), Microsoft.Extensions.Time.Testing (FakeTimeProvider)

---

### Task 14: Port interfaces (Abstractions)

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IAnswerProvider.cs`
- Create: `src/AIHelperNET.Application/Abstractions/IAudioCaptureService.cs`
- Create: `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs`
- Create: `src/AIHelperNET.Application/Abstractions/IScreenOcrService.cs`
- Create: `src/AIHelperNET.Application/Abstractions/IGlobalHotkeyService.cs`
- Create: `src/AIHelperNET.Application/Abstractions/ISecretStore.cs`
- Create: `src/AIHelperNET.Application/Abstractions/ISessionRepository.cs`
- Create: `src/AIHelperNET.Application/Abstractions/IUnitOfWork.cs`
- Create: `src/AIHelperNET.Application/Abstractions/ISettingsStore.cs`
- Create: `src/AIHelperNET.Application/Abstractions/IAnswerStreamSink.cs`

No tests for interfaces — they are contracts tested through their implementations.

- [ ] **Step 1: Create supporting DTOs needed by abstractions**

```csharp
// src/AIHelperNET.Application/Abstractions/AudioFrame.cs
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

public sealed record AudioFrame(float[] Samples, Speaker Speaker, DateTimeOffset CapturedAt);
public sealed record AudioDeviceSelection(string? MicDeviceId, string? LoopbackDeviceId);
public sealed record TranscriptSegment(string Text, Speaker Speaker, DateTimeOffset CapturedAt, float Confidence);
public enum WhisperModelSize { Tiny, Base, Small, Medium }
public enum AiBackend { Claude, Ollama }
```

```csharp
// src/AIHelperNET.Application/Abstractions/HotkeyTypes.cs
namespace AIHelperNET.Application.Abstractions;

public enum HotkeyId
{
    ToggleSession = 1,
    CaptureScreen = 2,
    GenerateAnswer = 3,
    CopyAnswer = 4,
    ToggleOverlay = 5
}

public enum ModifierKeys : uint { None = 0, Alt = 1, Ctrl = 2, Shift = 4, Win = 8 }
public enum VirtualKey : uint { Space = 0x20, S = 0x53, Q = 0x51, C = 0x43, H = 0x48 }
```

- [ ] **Step 2: Create IAnswerProvider**

```csharp
// src/AIHelperNET.Application/Abstractions/IAnswerProvider.cs
namespace AIHelperNET.Application.Abstractions;

public interface IAnswerProvider
{
    AiBackend Backend { get; }
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerPrompt prompt, CancellationToken ct);
}
```

Note: `AnswerPrompt` is defined in Phase 3 Task 16. Add a forward reference comment here — it lives in `AIHelperNET.Application.Answers`.

Actually the `AnswerPrompt` is in `AIHelperNET.Application.Answers` namespace. To avoid circular references, define `AnswerPrompt` first (Task 16), then `IAnswerProvider` can reference it. For now create a placeholder that will compile once AnswerPrompt is added:

Replace the IAnswerProvider above with the correct using once AnswerPrompt is in place:

```csharp
// src/AIHelperNET.Application/Abstractions/IAnswerProvider.cs
using AIHelperNET.Application.Answers;

namespace AIHelperNET.Application.Abstractions;

public interface IAnswerProvider
{
    AiBackend Backend { get; }
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerPrompt prompt, CancellationToken ct);
}
```

- [ ] **Step 3: Create IAudioCaptureService**

```csharp
// src/AIHelperNET.Application/Abstractions/IAudioCaptureService.cs
namespace AIHelperNET.Application.Abstractions;

public interface IAudioCaptureService
{
    IAsyncEnumerable<AudioFrame> CaptureAsync(AudioDeviceSelection selection, CancellationToken ct);
}
```

- [ ] **Step 4: Create ITranscriptionService**

```csharp
// src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs
namespace AIHelperNET.Application.Abstractions;

public interface ITranscriptionService
{
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize model, CancellationToken ct);
}
```

- [ ] **Step 5: Create IScreenOcrService**

```csharp
// src/AIHelperNET.Application/Abstractions/IScreenOcrService.cs
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

public interface IScreenOcrService
{
    Task<Result<string>> CaptureAndReadAsync(CancellationToken ct);
}
```

- [ ] **Step 6: Create IGlobalHotkeyService**

```csharp
// src/AIHelperNET.Application/Abstractions/IGlobalHotkeyService.cs
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

public interface IGlobalHotkeyService
{
    Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key);
    void UnregisterAll();
    event EventHandler<HotkeyId> HotkeyPressed;
}
```

- [ ] **Step 7: Create ISecretStore**

```csharp
// src/AIHelperNET.Application/Abstractions/ISecretStore.cs
using System.Net;
using System.Security;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

public interface ISecretStore
{
    Result SaveApiKey(SecureString key);
    Result<SecureString> GetApiKey();
    Result DeleteApiKey();
    bool HasApiKey();
}
```

- [ ] **Step 8: Create ISessionRepository**

```csharp
// src/AIHelperNET.Application/Abstractions/ISessionRepository.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

public interface ISessionRepository
{
    Task<Result<Session>> GetAsync(SessionId id, CancellationToken ct);
    Task AddAsync(Session session, CancellationToken ct);
    Task<IReadOnlyList<SessionSummaryDto>> GetHistoryAsync(int take, CancellationToken ct);
    void Update(Session session);
}
```

- [ ] **Step 9: Create IUnitOfWork**

```csharp
// src/AIHelperNET.Application/Abstractions/IUnitOfWork.cs
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken ct);
}
```

- [ ] **Step 10: Create ISettingsStore**

```csharp
// src/AIHelperNET.Application/Abstractions/ISettingsStore.cs
using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Application.Abstractions;

public interface ISettingsStore
{
    Task<AppSettingsDto> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettingsDto settings, CancellationToken ct);
}
```

- [ ] **Step 11: Create IAnswerStreamSink**

```csharp
// src/AIHelperNET.Application/Abstractions/IAnswerStreamSink.cs
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Abstractions;

public interface IAnswerStreamSink
{
    ValueTask PushAsync(AnswerId answerId, string chunk, CancellationToken ct);
}
```

- [ ] **Step 12: Build — should succeed**

```powershell
dotnet build src/AIHelperNET.Application/
```

Note: will fail until DTOs (Task 15) and AnswerPrompt (Task 16) are added. Proceed to Task 15 immediately.

---

### Task 15: DTOs

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Dtos/SessionDto.cs`
- Create: `src/AIHelperNET.Application/Sessions/Dtos/SessionSummaryDto.cs`
- Create: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Create: `src/AIHelperNET.Application/Sessions/Dtos/AudioDeviceDto.cs`

- [ ] **Step 1: Create DTOs**

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/SessionDto.cs
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

public sealed record SessionDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SessionState State,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    IReadOnlyList<TranscriptItemDto> Transcript,
    IReadOnlyList<DetectedQuestionDto> Questions,
    IReadOnlyList<GeneratedAnswerDto> Answers);

public sealed record TranscriptItemDto(
    TranscriptItemId Id, Speaker Speaker, string Text,
    DateTimeOffset Timestamp, float Confidence);

public sealed record DetectedQuestionDto(
    QuestionId Id, string Text, QuestionSource Source, DateTimeOffset DetectedAt);

public sealed record GeneratedAnswerDto(
    AnswerId Id, QuestionId QuestionId, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, AnswerStatus Status, string Content);
```

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/SessionSummaryDto.cs
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Sessions.Dtos;

public sealed record SessionSummaryDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SessionState State,
    int QuestionCount,
    int AnswerCount);
```

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId);
```

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/AudioDeviceDto.cs
namespace AIHelperNET.Application.Sessions.Dtos;

public sealed record AudioDeviceDto(string Id, string Name, bool IsDefault);
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Application/
```

---

### Task 16: AnswerPrompt + PromptBuilderService

**Files:**
- Create: `src/AIHelperNET.Application/Answers/AnswerPrompt.cs`
- Create: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Create: `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class PromptBuilderServiceTests
{
    private readonly PromptBuilderService _sut = new();
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Build_IncludesCodeProfileInSystem()
    {
        var profile = CodeProfile.Empty with { ProgrammingLanguage = "C#", Database = "PostgreSQL" };
        var question = DetectedQuestion.Create("Explain async/await", QuestionSource.Audio, Now);

        var prompt = _sut.Build(profile, AnswerSettings.Default, question);

        prompt.System.Should().Contain("C#");
        prompt.System.Should().Contain("PostgreSQL");
    }

    [Fact]
    public void Build_IncludesAnswerSettingsInSystem()
    {
        var settings = AnswerSettings.Default with { Style = AnswerStyle.CodeFirst };
        var question = DetectedQuestion.Create("Write a LINQ query", QuestionSource.Audio, Now);

        var prompt = _sut.Build(CodeProfile.Empty, settings, question);

        prompt.System.Should().Contain("CodeFirst");
    }

    [Fact]
    public void Build_IncludesQuestionInUser()
    {
        var question = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, Now);
        var prompt = _sut.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        prompt.User.Should().Contain("What is CQRS?");
    }

    [Fact]
    public void Build_WithScreenContext_IncludesOcrInUser()
    {
        var question = DetectedQuestion.Create("Debug this", QuestionSource.Audio, Now);
        var prompt = _sut.Build(CodeProfile.Empty, AnswerSettings.Default, question, "NullReferenceException at line 42");
        prompt.User.Should().Contain("NullReferenceException at line 42");
    }

    [Fact]
    public void Build_NoRagContext_SystemHasNoKnowledgeBase()
    {
        var question = DetectedQuestion.Create("Explain SOLID", QuestionSource.Audio, Now);
        var prompt = _sut.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        prompt.System.Should().NotContain("Knowledge base");
        prompt.System.Should().NotContain("Retrieved context");
        prompt.System.Should().NotContain("RAG");
    }

    [Fact]
    public void Build_EmptyProfile_NoProfileLines()
    {
        var question = DetectedQuestion.Create("Tell me about yourself", QuestionSource.Audio, Now);
        var prompt = _sut.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        // Should not have null/empty lines from empty profile
        prompt.System.Should().NotContain(": \n");
    }

    [Theory]
    [InlineData(AnswerLength.VeryShort, 200)]
    [InlineData(AnswerLength.Short, 400)]
    [InlineData(AnswerLength.Medium, 800)]
    [InlineData(AnswerLength.Detailed, 1500)]
    [InlineData(AnswerLength.DeepDive, 3000)]
    public void Build_MapsLengthToTokens(AnswerLength length, int expected)
    {
        var settings = AnswerSettings.Default with { Length = length };
        var question = DetectedQuestion.Create("Test question?", QuestionSource.Audio, Now);
        var prompt = _sut.Build(CodeProfile.Empty, settings, question);
        prompt.MaxTokens.Should().Be(expected);
    }
}
```

- [ ] **Step 2: Run — compile error**

```powershell
dotnet test tests/AIHelperNET.Application.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Create AnswerPrompt**

```csharp
// src/AIHelperNET.Application/Answers/AnswerPrompt.cs
namespace AIHelperNET.Application.Answers;

public sealed record AnswerPrompt(
    string System,
    string User,
    string OutputLanguage,
    int MaxTokens);
```

- [ ] **Step 4: Implement PromptBuilderService**

```csharp
// src/AIHelperNET.Application/Answers/PromptBuilderService.cs
using System.Text;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Answers;

public sealed class PromptBuilderService
{
    public AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        DetectedQuestion question,
        string? screenContext = null)
    {
        var system = new StringBuilder();
        system.AppendLine("You are an expert technical interview assistant. " +
            "Answer the candidate's interview question concisely and correctly.");

        AppendCodeProfile(system, profile);
        AppendAnswerSettings(system, settings);

        var user = new StringBuilder();
        user.AppendLine($"Question: {question.Text}");
        if (!string.IsNullOrWhiteSpace(screenContext))
            user.AppendLine($"\nOn-screen context (OCR):\n{screenContext}");

        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: MapLengthToTokens(settings.Length));
    }

    private static void AppendCodeProfile(StringBuilder sb, CodeProfile p)
    {
        sb.AppendLine("\n# Candidate technical profile (use this stack in code/examples):");
        AppendIf(sb, "Programming language", p.ProgrammingLanguage);
        AppendIf(sb, "Backend framework",    p.BackendFramework);
        AppendIf(sb, "Frontend framework",   p.FrontendFramework);
        AppendIf(sb, "Database",             p.Database);
        AppendIf(sb, "Cloud/DevOps",         p.CloudDevOps);
        AppendIf(sb, "Messaging",            p.Messaging);
        AppendIf(sb, "Architecture style",   p.ArchitectureStyle);
        AppendIf(sb, "Testing framework",    p.TestingFramework);
        AppendIf(sb, "Notes",                p.CustomNotes);
    }

    private static void AppendAnswerSettings(StringBuilder sb, AnswerSettings s)
    {
        sb.AppendLine("\n# Answer requirements:");
        sb.AppendLine($"- Length: {s.Length}");
        sb.AppendLine($"- Complexity: {s.Complexity}");
        sb.AppendLine($"- Style: {s.Style}");
        sb.AppendLine($"- Tone: {s.Tone}");
        sb.AppendLine($"- Format: {s.Format}");
        sb.AppendLine($"- Output language: {s.OutputLanguage}");
    }

    private static void AppendIf(StringBuilder sb, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) sb.AppendLine($"- {label}: {value}");
    }

    private static int MapLengthToTokens(AnswerLength length) => length switch
    {
        AnswerLength.VeryShort => 200,
        AnswerLength.Short     => 400,
        AnswerLength.Medium    => 800,
        AnswerLength.Detailed  => 1500,
        AnswerLength.DeepDive  => 3000,
        _                      => 800
    };
}
```

- [ ] **Step 5: Run — all pass**

```powershell
dotnet test tests/AIHelperNET.Application.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 6: Commit**

```powershell
git add src/AIHelperNET.Application/Answers/ tests/AIHelperNET.Application.Tests/Answers/
git commit -m "feat(application): add AnswerPrompt and PromptBuilderService"
```

---

### Task 17: Commands, queries, and handlers

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Commands/StartSessionCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/StopSessionCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/UpdateSettingsCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/SaveApiKeyCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/CaptureScreenCommand.cs`
- Create: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`
- Create: `src/AIHelperNET.Application/Sessions/Queries/GetCurrentSessionQuery.cs`
- Create: `src/AIHelperNET.Application/Sessions/Queries/GetSessionHistoryQuery.cs`
- Create: `src/AIHelperNET.Application/Sessions/Queries/GetSettingsQuery.cs`
- Create: `src/AIHelperNET.Application/Sessions/Queries/GetAudioDevicesQuery.cs`
- Create: `tests/AIHelperNET.Application.Tests/Sessions/StartSessionHandlerTests.cs`

- [ ] **Step 1: Write failing StartSession handler test**

```csharp
// tests/AIHelperNET.Application.Tests/Sessions/StartSessionHandlerTests.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class StartSessionHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_AddsSessionAndReturnsDto()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var mapper = new SessionMapper();

        var handler = new StartSessionHandler(repo, uow, clock, mapper);
        var cmd = new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(SessionState.Active);
        await repo.Received(1).AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PersistenceFails_ReturnsFailure()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Fail("db error"));
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var mapper = new SessionMapper();

        var handler = new StartSessionHandler(repo, uow, clock, mapper);
        var result = await handler.Handle(new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run — compile errors expected**

```powershell
dotnet test tests/AIHelperNET.Application.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Create SessionMapper (Mapperly)**

```csharp
// src/AIHelperNET.Application/Sessions/SessionMapper.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using Riok.Mapperly.Abstractions;

namespace AIHelperNET.Application.Sessions;

[Mapper]
public sealed partial class SessionMapper
{
    public partial SessionDto ToDto(Session session);
    public partial SessionSummaryDto ToSummary(Session session);
}
```

Note: Mapperly generates `QuestionCount` and `AnswerCount` from `session.Questions.Count` and `session.Answers.Count` automatically via name matching if we add computed properties to the aggregate, OR we can map manually. Since `SessionSummaryDto` has `QuestionCount` and `AnswerCount` but `Session` has `Questions` and `Answers` collections, add a manual map:

Replace with:

```csharp
// src/AIHelperNET.Application/Sessions/SessionMapper.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using Riok.Mapperly.Abstractions;

namespace AIHelperNET.Application.Sessions;

[Mapper]
public sealed partial class SessionMapper
{
    public partial SessionDto ToDto(Session session);

    [MapProperty(nameof(Session.Questions) + "." + nameof(IReadOnlyList<object>.Count), nameof(SessionSummaryDto.QuestionCount))]
    [MapProperty(nameof(Session.Answers) + "." + nameof(IReadOnlyList<object>.Count), nameof(SessionSummaryDto.AnswerCount))]
    public partial SessionSummaryDto ToSummary(Session session);
}
```

If Mapperly cannot resolve the Count properties automatically, use a manual method instead:

```csharp
// src/AIHelperNET.Application/Sessions/SessionMapper.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using Riok.Mapperly.Abstractions;

namespace AIHelperNET.Application.Sessions;

[Mapper]
public sealed partial class SessionMapper
{
    public partial SessionDto ToDto(Session session);

    public SessionSummaryDto ToSummary(Session session) => new(
        session.Id,
        session.StartedAt,
        session.EndedAt,
        session.State,
        session.Questions.Count,
        session.Answers.Count);
}
```

Use the manual version — it's clear and avoids Mapperly edge cases with collection counts.

- [ ] **Step 4: Create StartSession command + handler**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/StartSessionCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record StartSessionCommand(
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile) : IRequest<Result<SessionDto>>;

public sealed class StartSessionHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    SessionMapper mapper) : IRequestHandler<StartSessionCommand, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(
        StartSessionCommand command, CancellationToken ct)
    {
        var create = Session.Create(command.AnswerSettings, command.CodeProfile, clock.GetUtcNow());
        if (create.IsFailed)
            return Result.Fail(create.Error);

        var session = create.Value;
        await repository.AddAsync(session, ct);
        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailed) return save;

        return Result.Ok(mapper.ToDto(session));
    }
}
```

- [ ] **Step 5: Create StopSession command + handler**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/StopSessionCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record StopSessionCommand(SessionId SessionId) : IRequest<Result>;

public sealed class StopSessionHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<StopSessionCommand, Result>
{
    public async ValueTask<Result> Handle(StopSessionCommand command, CancellationToken ct)
    {
        var get = await repository.GetAsync(command.SessionId, ct);
        if (get.IsFailed) return get;

        var stop = get.Value.Stop(clock.GetUtcNow());
        if (stop.IsFailed) return Result.Fail(stop.Error);

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 6: Create UpdateSettings command + handler**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/UpdateSettingsCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.ValueObjects;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record UpdateSettingsCommand(
    SessionId SessionId,
    AnswerSettings? AnswerSettings,
    CodeProfile? CodeProfile) : IRequest<Result>;

public sealed class UpdateSettingsHandler(
    ISessionRepository repository,
    IUnitOfWork unitOfWork) : IRequestHandler<UpdateSettingsCommand, Result>
{
    public async ValueTask<Result> Handle(UpdateSettingsCommand command, CancellationToken ct)
    {
        var get = await repository.GetAsync(command.SessionId, ct);
        if (get.IsFailed) return get;

        if (command.AnswerSettings is not null)
            get.Value.UpdateAnswerSettings(command.AnswerSettings);
        if (command.CodeProfile is not null)
            get.Value.UpdateCodeProfile(command.CodeProfile);

        repository.Update(get.Value);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 7: Create SaveApiKey command + handler**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/SaveApiKeyCommand.cs
using System.Security;
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record SaveApiKeyCommand(SecureString Key) : IRequest<Result>;

public sealed class SaveApiKeyHandler(ISecretStore secretStore)
    : IRequestHandler<SaveApiKeyCommand, Result>
{
    public ValueTask<Result> Handle(SaveApiKeyCommand command, CancellationToken ct)
        => ValueTask.FromResult(secretStore.SaveApiKey(command.Key));
}
```

- [ ] **Step 8: Create CaptureScreen command + handler**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/CaptureScreenCommand.cs
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public sealed record CaptureScreenCommand : IRequest<Result<string>>;

public sealed class CaptureScreenHandler(IScreenOcrService ocrService)
    : IRequestHandler<CaptureScreenCommand, Result<string>>
{
    public async ValueTask<Result<string>> Handle(CaptureScreenCommand command, CancellationToken ct)
        => await ocrService.CaptureAndReadAsync(ct);
}
```

- [ ] **Step 9: Create GenerateAnswer command + handler**

```csharp
// src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

public sealed record GenerateAnswerCommand(
    SessionId SessionId,
    QuestionId QuestionId,
    string? ScreenContext = null) : IRequest<Result<AnswerId>>;

public sealed class GenerateAnswerHandler(
    ISessionRepository repository,
    IAnswerProvider answerProvider,
    PromptBuilderService promptBuilder,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateAnswerCommand, Result<AnswerId>>
{
    public async ValueTask<Result<AnswerId>> Handle(GenerateAnswerCommand cmd, CancellationToken ct)
    {
        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult<AnswerId>();
        var session = get.Value;

        var question = session.Questions.FirstOrDefault(q => q.Id == cmd.QuestionId);
        if (question is null) return Result.Fail("Question not found.");

        var start = session.StartAnswer(cmd.QuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = promptBuilder.Build(session.CodeProfile, session.AnswerSettings, question, cmd.ScreenContext);

        try
        {
            await foreach (var chunk in answerProvider.StreamAnswerAsync(prompt, ct))
            {
                answer.AppendChunk(chunk);
                await streamSink.PushAsync(answer.Id, chunk, ct);
            }
            answer.Complete(clock.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            answer.Cancel(clock.GetUtcNow());
        }
        catch (Exception)
        {
            answer.Fail(clock.GetUtcNow());
        }

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(ct);
        return save.IsFailed ? save.ToResult<AnswerId>() : Result.Ok(answer.Id);
    }
}
```

- [ ] **Step 10: Create queries**

```csharp
// src/AIHelperNET.Application/Sessions/Queries/GetCurrentSessionQuery.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

public sealed record GetCurrentSessionQuery(SessionId Id) : IRequest<Result<SessionDto>>;

public sealed class GetCurrentSessionHandler(ISessionRepository repository, SessionMapper mapper)
    : IRequestHandler<GetCurrentSessionQuery, Result<SessionDto>>
{
    public async ValueTask<Result<SessionDto>> Handle(GetCurrentSessionQuery query, CancellationToken ct)
    {
        var get = await repository.GetAsync(query.Id, ct);
        return get.IsSuccess ? Result.Ok(mapper.ToDto(get.Value)) : get.ToResult<SessionDto>();
    }
}
```

```csharp
// src/AIHelperNET.Application/Sessions/Queries/GetSessionHistoryQuery.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

public sealed record GetSessionHistoryQuery(int Take = 50) : IRequest<Result<IReadOnlyList<SessionSummaryDto>>>;

public sealed class GetSessionHistoryHandler(ISessionRepository repository)
    : IRequestHandler<GetSessionHistoryQuery, Result<IReadOnlyList<SessionSummaryDto>>>
{
    public async ValueTask<Result<IReadOnlyList<SessionSummaryDto>>> Handle(
        GetSessionHistoryQuery query, CancellationToken ct)
    {
        var history = await repository.GetHistoryAsync(query.Take, ct);
        return Result.Ok(history);
    }
}
```

```csharp
// src/AIHelperNET.Application/Sessions/Queries/GetSettingsQuery.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

public sealed record GetSettingsQuery : IRequest<Result<AppSettingsDto>>;

public sealed class GetSettingsHandler(ISettingsStore settingsStore)
    : IRequestHandler<GetSettingsQuery, Result<AppSettingsDto>>
{
    public async ValueTask<Result<AppSettingsDto>> Handle(GetSettingsQuery query, CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        return Result.Ok(settings);
    }
}
```

```csharp
// src/AIHelperNET.Application/Sessions/Queries/GetAudioDevicesQuery.cs
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

// Handler lives in Infrastructure (it needs NAudio); only the query is declared here.
public sealed record GetAudioDevicesQuery : IRequest<Result<IReadOnlyList<AudioDeviceDto>>>;
```

Note: `GetAudioDevicesQuery` handler is registered in Infrastructure since it requires NAudio device enumeration.

- [ ] **Step 11: Run StartSession tests — should pass now**

```powershell
dotnet test tests/AIHelperNET.Application.Tests/ --logger "console;verbosity=minimal"
```

Expected: StartSessionHandlerTests pass.

- [ ] **Step 12: Commit**

```powershell
git add src/AIHelperNET.Application/ tests/AIHelperNET.Application.Tests/Sessions/
git commit -m "feat(application): add commands, queries, and handlers"
```

---

### Task 18: Pipeline behaviors

**Files:**
- Create: `src/AIHelperNET.Application/Common/Behaviors/LoggingBehavior.cs`
- Create: `src/AIHelperNET.Application/Common/Behaviors/ValidationBehavior.cs`

No dedicated unit tests — behaviors are integration-tested through handlers in Application.Tests.

- [ ] **Step 1: Create LoggingBehavior**

```csharp
// src/AIHelperNET.Application/Common/Behaviors/LoggingBehavior.cs
using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace AIHelperNET.Application.Common.Behaviors;

public sealed class LoggingBehavior<TMessage, TResponse>(ILogger<TMessage> logger)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
{
    public async ValueTask<TResponse> Handle(
        TMessage message, CancellationToken ct, MessageHandlerDelegate<TMessage, TResponse> next)
    {
        var name = typeof(TMessage).Name;
        logger.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(message, ct);
            logger.LogInformation("Handled {Request} in {Elapsed}ms",
                name, Stopwatch.GetElapsedTime(sw).TotalMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled exception in {Request}", name);
            throw;
        }
    }
}
```

- [ ] **Step 2: Create ValidationBehavior**

```csharp
// src/AIHelperNET.Application/Common/Behaviors/ValidationBehavior.cs
using FluentResults;
using FluentValidation;
using Mediator;

namespace AIHelperNET.Application.Common.Behaviors;

public sealed class ValidationBehavior<TMessage, TResponse>(
    IEnumerable<IValidator<TMessage>> validators)
    : IPipelineBehavior<TMessage, TResponse>
    where TMessage : notnull, IMessage
    where TResponse : IResultBase, new()
{
    public async ValueTask<TResponse> Handle(
        TMessage message, CancellationToken ct, MessageHandlerDelegate<TMessage, TResponse> next)
    {
        if (!validators.Any()) return await next(message, ct);

        var context = new ValidationContext<TMessage>(message);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0) return await next(message, ct);

        var result = new TResponse();
        foreach (var f in failures)
            result.Reasons.Add(new Error(f.ErrorMessage));
        return result;
    }
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/AIHelperNET.Application/
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Application/Common/
git commit -m "feat(application): add LoggingBehavior and ValidationBehavior"
```

---

### Task 19: DependencyInjection extension for Application

**Files:**
- Create: `src/AIHelperNET.Application/DependencyInjection.cs`

- [ ] **Step 1: Create DI extension**

```csharp
// src/AIHelperNET.Application/DependencyInjection.cs
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Common.Behaviors;
using AIHelperNET.Application.Sessions;
using FluentValidation;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediator(static options =>
            options.ServiceLifetime = ServiceLifetime.Scoped);

        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddSingleton(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);

        services.AddSingleton<SessionMapper>();
        services.AddSingleton<PromptBuilderService>();

        return services;
    }
}
```

- [ ] **Step 2: Build whole solution**

```powershell
dotnet build AIHelperNET.sln
```

Expected: 0 errors.

- [ ] **Step 3: Run all tests**

```powershell
dotnet test AIHelperNET.sln --logger "console;verbosity=minimal"
```

Expected: All current tests pass.

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Application/DependencyInjection.cs
git commit -m "feat(application): add DI extension, wire Mediator + behaviors + mapper"
```

---

**Phase 3 complete.** Continue with `2026-06-02-aihelper-phase4-infrastructure-persistence.md`.
