# AIHelperNET — Design Specification

**Date:** 2026-06-02
**Project:** AIHelperNET
**Location:** `D:\work\AIHelperNET`
**Document status:** Definitive implementation reference (v1)
**Target runtime:** .NET 10, WPF, Windows 10 1903+ / Windows 11

---

## 1. Purpose & Scope

AIHelperNET is a Windows desktop **live interview copilot**. During a live technical interview it:

1. Captures the interviewer's voice via microphone **and** system audio (WASAPI loopback).
2. Transcribes speech in real time (Whisper.net) with voice-activity detection and speaker labeling (`Me` = mic, `Other` = loopback).
3. Reads on-screen text via OCR (coding platforms, chat windows, problem statements).
4. Detects interview questions automatically using heuristics + Jaccard-similarity deduplication.
5. Generates **streaming** answers via Claude (online) or a local Ollama model (offline), switchable at runtime.
6. Displays answers in a **stealth overlay** invisible to screen-share/recording tools (Zoom, Teams, OBS, Google Meet) via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.
7. Persists every session (transcript, detected questions, generated answers, settings) to a local SQLite database.

### v1 scope (this document)

- Windows-only WPF desktop app.
- Live copilot workflow end-to-end.
- Online (Claude API, user's own key) **or** offline (Ollama) AI backend, switchable.
- Answer tuning (length/complexity/style/tone/format/language) and code profile presets.
- Stealth overlay, global hotkeys, session persistence.

### Explicitly out of scope for v1

- Practice / prep / mock-interview mode.
- Cross-platform (Avalonia) UI.
- Retrieval-augmented generation (RAG), knowledge base, embeddings, vector search.
- Telemetry / analytics / cloud sync.

### v2 (planned, not designed here)

- Avalonia UI head sharing the same Domain/Application/Infrastructure cores (presentation swap only).
- Practice/prep mode with curated question banks and self-rating.
- Session replay and answer-quality scoring.
- Optional RAG over the user's own notes (would introduce a `KnowledgeBase` aggregate + embedding provider — deliberately absent in v1).

---

## 2. Architecture — Lean Onion + CQRS

### 2.1 Dependency rule

Dependencies point inward only. Enforced by NetArchTest on every build.

```
        ┌─────────────────────────────────────────────┐
        │            AIHelperNET.App (WPF)             │  Presentation
        │   Windows, ViewModels, IHost composition     │
        └───────────────┬─────────────────────────────┘
                        │ depends on
        ┌───────────────▼─────────────────────────────┐
        │         AIHelperNET.Application               │  Use cases
        │  Mediator handlers (CQRS), behaviors,         │
        │  DTOs, abstractions (ports), PromptBuilder    │
        └───────────────┬─────────────────────────────┘
                        │ depends on
        ┌───────────────▼─────────────────────────────┐
        │           AIHelperNET.Domain                  │  Core
        │  Aggregates, entities, value objects,         │
        │  pure domain services (QuestionDetector)      │
        └───────────────────────────────────────────────┘
                        ▲
                        │ depends on (implements ports)
        ┌───────────────┴─────────────────────────────┐
        │        AIHelperNET.Infrastructure             │  Adapters
        │  NAudio, Whisper.net, Windows OCR, Claude,    │
        │  Ollama, EF Core SQLite, hotkeys, security    │
        └───────────────────────────────────────────────┘
```

Rules:

- `Domain` depends on **nothing** (zero NuGet packages).
- `Application` depends only on `Domain`.
- `Infrastructure` depends on `Application` + `Domain` (it implements ports declared in Application).
- `App` (Presentation) depends on `Application` + `Domain` for types, and references `Infrastructure` **only** at the composition root for DI registration. No ViewModel may reference an Infrastructure concrete type.

### 2.2 Solution structure

```
AIHelperNET.sln
├── Directory.Build.props                  # Shared: net10.0, Nullable, ImplicitUsings, LangVersion latest, TreatWarningsAsErrors
├── src/
│   ├── AIHelperNET.Domain/                # Zero NuGet deps
│   ├── AIHelperNET.Application/           # Mediator, behaviors, DTOs, ports, PromptBuilder
│   ├── AIHelperNET.Infrastructure/        # Audio, OCR, AI, SQLite, hotkeys, security
│   └── AIHelperNET.App/                   # WPF: App.xaml, windows, ViewModels
└── tests/
    ├── AIHelperNET.Domain.Tests/          # Pure unit, no mocks (incl. QuestionDetector)
    ├── AIHelperNET.Application.Tests/     # Unit, NSubstitute for ports
    └── AIHelperNET.Integration.Tests/     # Real SQLite + Whisper tiny + Windows OCR
```

### 2.3 Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

The `App` project additionally sets `<UseWPF>true</UseWPF>` and `<OutputType>WinExe</OutputType>` and targets `net10.0-windows`. Infrastructure targets `net10.0-windows` (Windows.Media.Ocr, P/Invoke). Domain and Application stay on platform-neutral `net10.0` so they can be reused by the v2 Avalonia head.

### 2.4 Reference projects

| Reference | What is reused |
|---|---|
| `D:\work\InterviewHelper` (Python/Electron) | Feature set; audio capture, VAD, Whisper, Jaccard dedup, question heuristics, OCR preprocessing, overlay behavior, hotkey map, answer-tuning taxonomy, code-profile schema |
| `D:\work\Task Manager` (.NET 10) | Onion + NetArchTest, Mediator.SourceGenerator CQRS, FluentResults, FluentValidation pipeline, Mapperly, Serilog, rich domain model, IUnitOfWork, Options pattern, per-layer DI extensions, xUnit/FluentAssertions/NSubstitute |

---

## 3. Domain Layer (`AIHelperNET.Domain`)

Pure C#, zero NuGet dependencies. Rich model: private setters, factory methods returning `Result`, behavior methods that enforce invariants. The Domain does **not** reference FluentResults (an external package); it ships a tiny internal `Result`/`Result<T>` to keep the "zero NuGet" rule. (Application maps Domain results into FluentResults at the boundary, or — simpler and recommended — Domain exposes its own minimal result type and Application adopts the same shape. This spec uses an internal lightweight `Result` in Domain and FluentResults in Application; the conversion is a one-liner in handlers.)

> **Decision:** To avoid double result types, Domain defines `DomainResult` / `DomainResult<T>` (struct-based, no allocations) used only for invariant enforcement inside aggregates. Application-level handlers return `FluentResults.Result<T>`. The mapping is trivial and isolated to handlers.

### 3.1 Aggregate root: `Session`

```csharp
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
    public AnswerSettings AnswerSettings { get; private set; }
    public CodeProfile CodeProfile { get; private set; }

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
        AnswerSettings answerSettings,
        CodeProfile codeProfile,
        DateTimeOffset now)
    {
        if (answerSettings is null) return DomainResult.Fail<Session>("Answer settings required.");
        if (codeProfile is null) return DomainResult.Fail<Session>("Code profile required.");
        return DomainResult.Ok(new Session(SessionId.New(), now, answerSettings, codeProfile));
    }

    public DomainResult AddTranscriptItem(TranscriptItem item)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot add transcript to a stopped session.");
        // Deduplication is performed by the QuestionDetector service before questions are added;
        // raw transcript items are appended as-is (speaker-labeled).
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

    // EF Core materialization ctor
    private Session() { }
}

public enum SessionState { Active, Stopped }
```

### 3.2 Entities

```csharp
public sealed class TranscriptItem
{
    public TranscriptItemId Id { get; }
    public Speaker Speaker { get; }            // Me | Other
    public string Text { get; }
    public DateTimeOffset Timestamp { get; }
    public float Confidence { get; }

    private TranscriptItem(TranscriptItemId id, Speaker speaker, string text,
        DateTimeOffset ts, float confidence)
        => (Id, Speaker, Text, Timestamp, Confidence) = (id, speaker, text, ts, confidence);

    public static TranscriptItem Create(Speaker speaker, string text,
        DateTimeOffset ts, float confidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TranscriptItem(TranscriptItemId.New(), speaker, text.Trim(), ts, confidence);
    }
}

public enum Speaker { Me, Other }

public sealed class DetectedQuestion
{
    public QuestionId Id { get; }
    public string Text { get; }
    public QuestionSource Source { get; }      // Audio | Ocr | Manual
    public DateTimeOffset DetectedAt { get; }

    private DetectedQuestion(QuestionId id, string text, QuestionSource src, DateTimeOffset at)
        => (Id, Text, Source, DetectedAt) = (id, text, src, at);

    public static DetectedQuestion Create(string text, QuestionSource source, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new DetectedQuestion(QuestionId.New(), text.Trim(), source, at);
    }
}

public enum QuestionSource { Audio, Ocr, Manual }

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

    public void Complete(DateTimeOffset at)
    {
        Status = AnswerStatus.Completed;
        CompletedAt = at;
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status == AnswerStatus.Streaming) Status = AnswerStatus.Cancelled;
        CompletedAt = at;
    }

    public void Fail(DateTimeOffset at)
    {
        Status = AnswerStatus.Failed;
        CompletedAt = at;
    }
}

public enum AnswerStatus { Streaming, Completed, Cancelled, Failed }
```

### 3.3 Strongly-typed IDs

`readonly record struct` wrappers over `Guid` to prevent primitive-obsession bugs (passing a `QuestionId` where a `SessionId` is expected becomes a compile error).

```csharp
public readonly record struct SessionId(Guid Value)
{
    public static SessionId New() => new(Guid.CreateVersion7());
}
public readonly record struct TranscriptItemId(Guid Value)
{ public static TranscriptItemId New() => new(Guid.CreateVersion7()); }
public readonly record struct QuestionId(Guid Value)
{ public static QuestionId New() => new(Guid.CreateVersion7()); }
public readonly record struct AnswerId(Guid Value)
{ public static AnswerId New() => new(Guid.CreateVersion7()); }
```

`Guid.CreateVersion7()` gives time-ordered IDs (better SQLite index locality).

### 3.4 Value objects

```csharp
public sealed record AnswerSettings(
    AnswerLength Length,
    AnswerComplexity Complexity,
    AnswerStyle Style,
    AnswerTone Tone,
    AnswerFormat Format,
    string OutputLanguage)
{
    public static AnswerSettings Default => new(
        AnswerLength.Medium, AnswerComplexity.Balanced, AnswerStyle.Interview,
        AnswerTone.Confident, AnswerFormat.ExplanationPlusCode, "English");
}

public enum AnswerLength    { VeryShort, Short, Medium, Detailed, DeepDive }
public enum AnswerComplexity{ Simple, Balanced, Advanced, Senior }
public enum AnswerStyle     { Natural, Interview, Technical, StepByStep, CodeFirst, Architecture, Debugging }
public enum AnswerTone      { Calm, Confident, Professional, Friendly }
public enum AnswerFormat    { VerbalOnly, ExplanationPlusCode, CodeOnly, ExplanationPlusNotes }

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
    public static CodeProfile Empty => new(null, null, null, null, null, null, null, null, null);
}
```

Records give value equality and immutability for free; "edit settings" = construct a new record and call `Session.UpdateAnswerSettings`.

### 3.5 Pure domain service: `QuestionDetector` (FIX #1)

`QuestionDetector` lives in the **Domain layer** and is a **pure** service: no I/O, no infrastructure, no DI dependencies. It is unit-tested in `AIHelperNET.Domain.Tests` with **no mocks**. (This corrects the earlier draft, which mistakenly placed it in Application.)

Responsibilities:

1. Decide whether a transcript fragment is a question, using heuristics:
   - Ends with `?`.
   - Starts with an interrogative (`what, why, how, when, where, which, who, can, could, would, will, do, does, did, is, are`).
   - Starts with an imperative/technical verb (`explain, describe, write, implement, design, compare, optimize, refactor, debug, walk (me through)`).
2. Deduplicate against recently detected questions via **Jaccard similarity** on word sets; reject if similarity to any recent question ≥ **0.6**.

```csharp
namespace AIHelperNET.Domain.Questions;

public sealed class QuestionDetector
{
    private const double DuplicateThreshold = 0.6;

    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    { "what","why","how","when","where","which","who","can","could","would",
      "will","do","does","did","is","are","should" };

    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    { "explain","describe","write","implement","design","compare","optimize",
      "refactor","debug","walk","tell","give","show" };

    /// <summary>Pure decision: is this text a NEW question not seen recently?</summary>
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

public sealed record QuestionDetectionResult(bool IsQuestion, bool IsDuplicate, string? NormalizedText)
{
    public static QuestionDetectionResult NotAQuestion() => new(false, false, null);
    public static QuestionDetectionResult Duplicate()    => new(true, true, null);
    public static QuestionDetectionResult NewQuestion(string t) => new(true, false, t);
}
```

The same `Jaccard` function is reused by the transcript-dedup heuristic at the Application/Infrastructure boundary (transcript fragments are deduped before being persisted), but the canonical implementation lives here in Domain.

---

## 4. Application Layer (`AIHelperNET.Application`)

CQRS via **Mediator.SourceGenerator** (zero-allocation, source-generated dispatch). Handlers return `FluentResults.Result<T>` — domain failures are never thrown. Cross-cutting concerns run as pipeline behaviors. This layer declares all **ports** (interfaces) that Infrastructure implements.

### 4.1 Ports (abstractions implemented by Infrastructure)

```csharp
namespace AIHelperNET.Application.Abstractions;

public interface IAnswerProvider
{
    /// <summary>Streams an answer token-by-token. Honors the cancellation token so a
    /// newer question can abort the in-flight generation.</summary>
    IAsyncEnumerable<string> StreamAnswerAsync(AnswerPrompt prompt, CancellationToken ct);
    AiBackend Backend { get; }   // Claude | Ollama
}

public enum AiBackend { Claude, Ollama }

public interface IAudioCaptureService
{
    /// <summary>Emits raw PCM frames tagged with the source (mic vs loopback).</summary>
    IAsyncEnumerable<AudioFrame> CaptureAsync(AudioDeviceSelection selection, CancellationToken ct);
}

public interface ITranscriptionService
{
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize model, CancellationToken ct);
}

public interface IScreenOcrService
{
    Task<Result<string>> CaptureAndReadAsync(CancellationToken ct);
}

public interface IGlobalHotkeyService
{
    Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key);
    void UnregisterAll();
    event EventHandler<HotkeyId> HotkeyPressed;
}

public interface ISecretStore
{
    Result SaveApiKey(SecureString key);          // Windows Credential Manager
    Result<SecureString> GetApiKey();
    Result DeleteApiKey();
    bool HasApiKey();
}

public interface ISessionRepository
{
    Task<Result<Session>> GetAsync(SessionId id, CancellationToken ct);
    Task AddAsync(Session session, CancellationToken ct);
    Task<IReadOnlyList<SessionSummary>> GetHistoryAsync(int take, CancellationToken ct);
    void Update(Session session);
}

public interface IUnitOfWork
{
    Task<Result> SaveChangesAsync(CancellationToken ct);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}
```

### 4.2 Commands & queries (CQRS)

**Commands:** `StartSession`, `StopSession`, `GenerateAnswer`, `CaptureScreen`, `UpdateSettings`, `SaveApiKey`.
**Queries:** `GetCurrentSession`, `GetSessionHistory`, `GetSettings`, `GetAudioDevices`.

```csharp
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
            return Result.Fail(create.Error);            // Domain → FluentResults at boundary

        var session = create.Value;
        await repository.AddAsync(session, ct);
        var save = await unitOfWork.SaveChangesAsync(ct);
        if (save.IsFailed) return save.ToResult<SessionDto>();

        return Result.Ok(mapper.ToDto(session));
    }
}
```

```csharp
namespace AIHelperNET.Application.Answers.Commands;

public sealed record GenerateAnswerCommand(SessionId SessionId, QuestionId QuestionId)
    : IRequest<Result<AnswerId>>;

/// <summary>
/// Starts a streaming answer. Cancellable: when a newer question arrives the orchestrator
/// cancels the prior token, which propagates to IAnswerProvider.StreamAnswerAsync.
/// Streamed chunks are pushed to the GeneratedAnswer aggregate and broadcast via IAnswerStreamSink.
/// </summary>
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

        var prompt = promptBuilder.Build(session.CodeProfile, session.AnswerSettings, question);

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
            // logged by LoggingBehavior; do not rethrow domain-adjacent failures
        }

        repository.Update(session);
        var save = await unitOfWork.SaveChangesAsync(ct);
        return save.IsFailed ? save.ToResult<AnswerId>() : Result.Ok(answer.Id);
    }
}
```

`IAnswerStreamSink.PushAsync` is a port; its Infrastructure/Presentation implementation marshals chunks to the overlay ViewModel on the UI thread.

```csharp
namespace AIHelperNET.Application.Sessions.Queries;

public sealed record GetAudioDevicesQuery : IRequest<Result<IReadOnlyList<AudioDeviceDto>>>;
public sealed record GetCurrentSessionQuery(SessionId Id) : IRequest<Result<SessionDto>>;
public sealed record GetSessionHistoryQuery(int Take = 50) : IRequest<Result<IReadOnlyList<SessionSummaryDto>>>;
public sealed record GetSettingsQuery : IRequest<Result<AppSettingsDto>>;
```

### 4.3 Pipeline behaviors

Mirroring the Task Manager pattern: `LoggingBehavior` (Serilog, structured) and `ValidationBehavior` (FluentValidation; converts validation failures into `Result.Fail` instead of throwing).

```csharp
public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
    where TResponse : ResultBase, new()
{
    public async ValueTask<TResponse> Handle(
        TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        if (!validators.Any()) return await next(request, ct);

        var context = new ValidationContext<TRequest>(request);
        var failures = validators
            .Select(v => v.Validate(context))
            .SelectMany(r => r.Errors)
            .Where(f => f is not null)
            .ToList();

        if (failures.Count == 0) return await next(request, ct);

        var result = new TResponse();
        foreach (var f in failures) result.Reasons.Add(new Error(f.ErrorMessage));
        return result;   // Result.Fail without throwing
    }
}

public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<TRequest> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async ValueTask<TResponse> Handle(
        TRequest request, MessageHandlerDelegate<TRequest, TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("Handling {Request}", name);
        var sw = Stopwatch.GetTimestamp();
        try
        {
            var response = await next(request, ct);
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

> **Note on PII:** prompts and transcripts can contain sensitive content. `LoggingBehavior` logs **request type names and timings only** — never request payloads. See §8.

### 4.4 PromptBuilderService — v1 uses CodeProfile + AnswerSettings only (FIX #2)

`PromptBuilderService` is an Application service. In **v1 it composes prompts from exactly two inputs: the `CodeProfile` and the `AnswerSettings`, plus the detected question and any OCR/transcript context passed in.** It performs **no** retrieval, **no** embeddings, and references **no** knowledge base or RAG store. (This corrects the earlier draft, which implied a KnowledgeBase dependency.) A v2 RAG variant would be an additive, separately-injected enricher; it is intentionally absent here.

```csharp
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

        AppendCodeProfile(system, profile);     // ONLY CodeProfile — no RAG, no KB
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
        _ => 800
    };
}

public sealed record AnswerPrompt(string System, string User, string OutputLanguage, int MaxTokens);
```

### 4.5 Mapping (Riok.Mapperly)

Source-generated, allocation-free DTO mapping.

```csharp
[Mapper]
public sealed partial class SessionMapper
{
    public partial SessionDto ToDto(Session session);
    public partial SessionSummaryDto ToSummary(Session session);
    [MapperIgnoreSource(nameof(Session.Transcript))]
    public partial SessionSummaryDto ToSummaryLite(Session session);
}
```

---

## 5. Infrastructure Layer (`AIHelperNET.Infrastructure`)

Implements every Application port. Targets `net10.0-windows`.

### 5.1 Audio capture — NAudio WASAPI (mic + loopback)

Two simultaneous captures: `WasapiCapture` for the default microphone (speaker `Me`) and `WasapiLoopbackCapture` for system playback (speaker `Other`). Both are resampled to **16 kHz mono float** (Whisper's expected input) and merged into one `IAsyncEnumerable<AudioFrame>` via a `Channel<AudioFrame>`.

```csharp
public sealed class NAudioCaptureService : IAudioCaptureService
{
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection, [EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true });

        using var mic = new WasapiCapture(GetDevice(selection.MicDeviceId));
        using var loopback = new WasapiLoopbackCapture(GetDevice(selection.LoopbackDeviceId));

        mic.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(ToFrame(e, Speaker.Me, mic.WaveFormat));
        loopback.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(ToFrame(e, Speaker.Other, loopback.WaveFormat));

        mic.StartRecording();
        loopback.StartRecording();
        using var reg = ct.Register(() =>
        {
            mic.StopRecording();
            loopback.StopRecording();
            channel.Writer.TryComplete();
        });

        await foreach (var frame in channel.Reader.ReadAllAsync(ct))
            yield return frame;
    }

    private static AudioFrame ToFrame(WaveInEventArgs e, Speaker speaker, WaveFormat sourceFormat)
        => new(Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, sourceFormat),
               speaker, DateTimeOffset.UtcNow);
    // GetDevice via MMDeviceEnumerator; GetAudioDevicesQuery enumerates the same.
}

public sealed record AudioFrame(float[] Samples, Speaker Speaker, DateTimeOffset CapturedAt);
```

`GetAudioDevicesQuery` is served by enumerating `MMDeviceEnumerator` render/capture endpoints.

### 5.2 Transcription — Whisper.net with VAD + dedup

Selectable model size (`tiny`/`base`/`small`/`medium`). VAD gates silence so Whisper only runs on speech. Adjacent near-duplicate segments are dropped using the Domain `QuestionDetector.Jaccard` helper (transcript dedup threshold tuned separately from question dedup).

```csharp
public sealed class WhisperTranscriptionService(IWhisperModelProvider models) : ITranscriptionService
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize size,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await using var processor = (await models.GetFactoryAsync(size, ct))
            .CreateBuilder().WithLanguage("auto").Build();

        var vad = new VoiceActivityDetector();
        string? lastEmitted = null;

        await foreach (var window in vad.AccumulateSpeechWindows(frames, ct))
        {
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;
                lastEmitted = seg.Text;
                yield return new TranscriptSegment(seg.Text, window.Speaker,
                    DateTimeOffset.UtcNow, seg.Probability);
            }
        }
    }

    private static bool IsNearDuplicate(string current, string? previous)
    {
        if (previous is null) return false;
        var a = current.ToLowerInvariant().Split(' ').ToHashSet();
        var b = previous.ToLowerInvariant().Split(' ').ToHashSet();
        return QuestionDetector.Jaccard(a, b) >= 0.85; // transcript-level dedup
    }
}

public enum WhisperModelSize { Tiny, Base, Small, Medium }
```

Models are downloaded on first use to `%LOCALAPPDATA%\AIHelperNET\models\` via `WhisperGgmlDownloader` and cached.

### 5.3 Screen OCR — Windows.Media.Ocr

Uses the **built-in Windows OCR engine** (`Windows.Media.Ocr.OcrEngine`), not Tesseract — zero native deps, ships with the OS, good accuracy for screen text. Capture is via `Graphics.CopyFromScreen` / `GraphicsCapture`; the bitmap is preprocessed (grayscale + upscale + contrast) before OCR.

```csharp
public sealed class WindowsOcrService : IScreenOcrService
{
    public async Task<Result<string>> CaptureAndReadAsync(CancellationToken ct)
    {
        using var bmp = ScreenGrabber.CapturePrimary();
        using var processed = ImagePreprocessor.Enhance(bmp);   // grayscale, 2x scale, contrast
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null) return Result.Fail("No OCR engine available for system languages.");

        var software = await processed.ToSoftwareBitmapAsync();
        var result = await engine.RecognizeAsync(software);
        return Result.Ok(result.Text);
    }
}
```

### 5.4 AI providers — Claude (online) & Ollama (offline)

Both implement `IAnswerProvider` and stream tokens. The active provider is resolved at runtime from settings; a `IAnswerProviderResolver` (or keyed DI) selects Claude vs Ollama.

```csharp
public sealed class ClaudeAnswerProvider(
    HttpClient http, ISecretStore secrets, IOptions<ClaudeOptions> options) : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Claude;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var key = secrets.GetApiKey();
        if (key.IsFailed) throw new InvalidOperationException("No API key configured.");

        using var request = BuildSseRequest(prompt, key.Value, options.Value); // SSE: stream=true
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var json = line["data:".Length..].Trim();
            if (json is "" or "[DONE]") continue;
            var delta = ClaudeSse.ParseTextDelta(json);   // content_block_delta → text
            if (!string.IsNullOrEmpty(delta)) yield return delta;
        }
    }
}

public sealed class OllamaAnswerProvider(IOllamaApiClient client, IOptions<OllamaOptions> options)
    : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Ollama;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        var request = new GenerateRequest
        {
            Model = options.Value.Model,                  // e.g. "llama3.1", "qwen2.5-coder"
            Prompt = $"{prompt.System}\n\n{prompt.User}",
            Stream = true
        };
        await foreach (var token in client.GenerateAsync(request, ct))
            if (token?.Response is { Length: > 0 } t) yield return t;
    }
}
```

The SecureString API key is read from Credential Manager **per request** and dereferenced only inside the request-building scope, then cleared (see §8). Claude SDK / OllamaSharp clients are registered as typed clients.

### 5.5 Persistence — EF Core + SQLite

Database at `%LOCALAPPDATA%\AIHelperNET\sessions.db`. The aggregate is mapped with owned types for value objects; collections are backing-field-mapped to respect encapsulation.

```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        var s = b.Entity<Session>();
        s.HasKey(x => x.Id);
        s.Property(x => x.Id).HasConversion(id => id.Value, v => new SessionId(v));
        s.OwnsOne(x => x.AnswerSettings);
        s.OwnsOne(x => x.CodeProfile);
        s.Navigation(x => x.Transcript).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.OwnsMany(x => x.Transcript);
        s.OwnsMany(x => x.Questions);
        s.OwnsMany(x => x.Answers);
    }
}

public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return Result.Ok(); }
        catch (DbUpdateException ex) { return Result.Fail(new Error("Persistence failed").CausedBy(ex)); }
    }
}
```

`SessionRepository` implements `ISessionRepository` over `AppDbContext` (eager-loads owned collections for `GetAsync`, projects to summaries for history).

### 5.6 Global hotkeys — RegisterHotKey P/Invoke

A hidden message-only window receives `WM_HOTKEY`. Hotkeys map to `HotkeyId` enum; the service raises `HotkeyPressed`, which the Presentation layer maps to Mediator commands.

| Hotkey | Action |
|---|---|
| `Ctrl+Shift+Space` | Toggle session (start/stop) |
| `Ctrl+Shift+S` | Capture screen (OCR) |
| `Ctrl+Shift+Q` | Generate answer for latest question |
| `Ctrl+Shift+C` | Copy current answer to clipboard |
| `Ctrl+Shift+H` | Toggle overlay visibility |

```csharp
public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private readonly HwndSource _source;          // message-only window
    public event EventHandler<HotkeyId>? HotkeyPressed;

    public Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key)
        => RegisterHotKey(_source.Handle, (int)id, (uint)modifiers, (uint)key)
            ? Result.Ok()
            : Result.Fail($"Failed to register hotkey {id} (already in use?).");

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY) { HotkeyPressed?.Invoke(this, (HotkeyId)wParam.ToInt32()); handled = true; }
        return IntPtr.Zero;
    }
}
```

### 5.7 Secret store — Windows Credential Manager

API keys are stored **only** in Windows Credential Manager via **AdysTech.CredentialManager**, transported as `SecureString`, and never written to `appsettings.json` or the database.

```csharp
public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string Target = "AIHelperNET:ClaudeApiKey";

    public Result SaveApiKey(SecureString key)
    {
        try
        {
            CredentialManager.SaveCredentials(Target,
                new NetworkCredential(string.Empty, key));  // SecureString overload
            return Result.Ok();
        }
        catch (Exception ex) { return Result.Fail(new Error("Could not save API key").CausedBy(ex)); }
    }

    public Result<SecureString> GetApiKey()
    {
        var cred = CredentialManager.GetCredentials(Target);
        return cred is null
            ? Result.Fail<SecureString>("No API key stored.")
            : Result.Ok(cred.SecurePassword);
    }

    public bool HasApiKey() => CredentialManager.GetCredentials(Target) is not null;
    public Result DeleteApiKey()
    {
        CredentialManager.RemoveCredentials(Target);
        return Result.Ok();
    }
}
```

### 5.8 DI registration

```csharp
public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={AppPaths.DatabaseFile}"));
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
        services.AddSingleton<IScreenOcrService, WindowsOcrService>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        services.AddHttpClient<ClaudeAnswerProvider>();
        services.AddSingleton<OllamaAnswerProvider>();
        services.AddSingleton<IAnswerProviderResolver, AnswerProviderResolver>(); // picks Claude|Ollama

        services.Configure<ClaudeOptions>(config.GetSection("Claude"));
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));
        return services;
    }
}
```

---

## 6. Presentation Layer (`AIHelperNET.App`)

WPF + **CommunityToolkit.Mvvm**. Generic Host (`IHost`) owns composition, configuration, logging, and lifetime. ViewModels depend only on `IMediator` and Application DTOs.

### 6.1 Host startup

```csharp
public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        _host = Host.CreateApplicationBuilder()
            .ConfigureAIHelper()       // see below
            .Build();
        await _host.StartAsync();
        await EnsureDatabaseAsync(_host);

        var shell = _host.Services.GetRequiredService<OverlayWindow>();
        shell.Show();
        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        using (_host) await _host.StopAsync();
        base.OnExit(e);
    }
}

public static class HostConfiguration
{
    public static HostApplicationBuilder ConfigureAIHelper(this HostApplicationBuilder b)
    {
        b.Services.AddSerilog((sp, lc) => lc
            .MinimumLevel.Information()
            .WriteTo.File(AppPaths.LogFile, rollingInterval: RollingInterval.Day));

        b.Services
            .AddDomain()
            .AddApplication()                       // registers Mediator + behaviors + validators + mappers
            .AddInfrastructure(b.Configuration)
            .AddPresentation();                     // windows + ViewModels
        return b;
    }
}
```

`AddDomain()` registers the pure domain services (`QuestionDetector`, `PromptBuilderService` lives in Application). `AddApplication()` wires `Mediator`, `ValidationBehavior`, `LoggingBehavior`, FluentValidation validators, and Mapperly mappers. `AddPresentation()` registers `OverlayWindow`, `SettingsWindow`, and ViewModels.

### 6.2 Stealth overlay window

`SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)` is applied in `OnSourceInitialized` (the earliest point a valid HWND exists). The window is borderless, topmost, click-through-optional, and translucent.

```csharp
public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
            Log.Warning("Display affinity not applied; overlay may be visible to capture.");
    }
}
```

> `WDA_EXCLUDEFROMCAPTURE` (Windows 10 2004+) excludes the window from screen capture while keeping it visible locally. On older builds, fall back to `WDA_MONITOR` (0x1) which renders the window black in captures.

### 6.3 ViewModel (CommunityToolkit.Mvvm source generators)

```csharp
public sealed partial class OverlayViewModel(IMediator mediator, IGlobalHotkeyService hotkeys)
    : ObservableObject
{
    [ObservableProperty] private string _currentAnswer = string.Empty;
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string _latestQuestion = string.Empty;

    private SessionId? _sessionId;
    private CancellationTokenSource? _answerCts;

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsSessionActive)
        {
            var result = await mediator.Send(new StartSessionCommand(
                AnswerSettings.Default, CodeProfile.Empty));
            if (result.IsSuccess) { _sessionId = result.Value.Id; IsSessionActive = true; }
        }
        else if (_sessionId is { } id)
        {
            await mediator.Send(new StopSessionCommand(id));
            IsSessionActive = false;
        }
    }

    [RelayCommand]
    private async Task GenerateAnswerAsync()
    {
        if (_sessionId is not { } id) return;
        _answerCts?.Cancel();                         // cancel previous in-flight answer
        _answerCts = new CancellationTokenSource();
        CurrentAnswer = string.Empty;
        await mediator.Send(new GenerateAnswerCommand(id, /* latest QuestionId */ default),
            _answerCts.Token);
    }

    // Subscribed to IAnswerStreamSink → chunks marshaled to UI thread, appended to CurrentAnswer.
}
```

`IAnswerStreamSink` is implemented in Presentation (or Infrastructure) to push chunks onto the UI dispatcher and append to `CurrentAnswer`, giving live token-by-token rendering. The hotkey service's `HotkeyPressed` event is mapped to these relay commands at startup.

### 6.4 Live data flow (end-to-end)

```
Ctrl+Shift+Space ──► ToggleSession ──► StartSessionCommand ──► Session.Create ──► SQLite
        │
        ▼ (session active)
[Mic]  WasapiCapture ─┐
                      ├─► AudioFrame channel ─► Whisper (VAD) ─► TranscriptSegment
[Sys]  WasapiLoopback ┘                                              │
                                                                     ▼
                                  Session.AddTranscriptItem (Me/Other speaker label)
                                                                     │
                          QuestionDetector.Evaluate (heuristics + Jaccard 0.6 dedup)  ◄── Domain
                                                                     │ (new question)
                                                  Session.AddDetectedQuestion ─► UI shows question
        │
Ctrl+Shift+Q ──► GenerateAnswerCommand
        │            │
        │            ▼
        │   PromptBuilder.Build(CodeProfile, AnswerSettings, question, [OCR ctx])   ◄── no RAG
        │            │
        │            ▼
        │   IAnswerProvider.StreamAnswerAsync (Claude SSE | Ollama)  ── cancellable
        │            │   tokens
        │            ▼
        │   GeneratedAnswer.AppendChunk ─► IAnswerStreamSink ─► OverlayViewModel.CurrentAnswer (live)
        │
Ctrl+Shift+S ──► CaptureScreenCommand ─► Windows OCR ─► screen context fed into next prompt
Ctrl+Shift+C ──► copy CurrentAnswer to clipboard
Ctrl+Shift+H ──► toggle OverlayWindow visibility
```

---

## 7. Configuration (Options pattern)

Non-secret settings only in `appsettings.json`; secrets in Credential Manager.

```json
{
  "Claude":  { "BaseUrl": "https://api.anthropic.com", "Model": "claude-sonnet-4-6", "Version": "2023-06-01" },
  "Ollama":  { "BaseUrl": "http://localhost:11434", "Model": "qwen2.5-coder:7b" },
  "Audio":   { "DefaultModel": "Base", "SampleRate": 16000 },
  "Backend": { "Active": "Claude" }
}
```

User-editable runtime settings (active backend, answer settings, code profile, audio devices, Whisper model) persist to `%LOCALAPPDATA%\AIHelperNET\settings.json` via `ISettingsStore`, separate from immutable `appsettings.json`.

`AppPaths`:
- DB: `%LOCALAPPDATA%\AIHelperNET\sessions.db`
- Logs: `%LOCALAPPDATA%\AIHelperNET\logs\log-.txt`
- Whisper models: `%LOCALAPPDATA%\AIHelperNET\models\`

---

## 8. Security Checklist

| # | Control | Implementation |
|---|---|---|
| 1 | API key never in plaintext config | Stored only in Windows Credential Manager (`AdysTech.CredentialManager`). Never in `appsettings.json`, DB, or logs. |
| 2 | Key transported as SecureString | `ISecretStore` returns `SecureString`; dereferenced via `Marshal.SecureStringToBSTR` only inside the HTTP request scope, then `ZeroFreeBSTR` immediately. |
| 3 | No payload logging | `LoggingBehavior` logs request **type names + timings only** — never prompts, transcripts, or answers (PII/sensitive). |
| 4 | Local-only data | SQLite + settings + logs under `%LOCALAPPDATA%`, per-user. No cloud sync, no telemetry. |
| 5 | No telemetry / analytics | Zero outbound calls except the user-selected AI backend. Ollama path is fully offline. |
| 6 | HTTPS enforced | Claude calls over TLS; `HttpClient` rejects non-success; no certificate bypass. |
| 7 | Stealth overlay | `WDA_EXCLUDEFROMCAPTURE` so the overlay is invisible to screen-share/recording. Fallback `WDA_MONITOR` on older Windows. |
| 8 | Least privilege | App runs as standard user; no admin elevation. Credential Manager scope is current user. |
| 9 | Input validation | FluentValidation on every command; failures return `Result.Fail`, never throw, no partial state. |
| 10 | Secret lifetime | API key read per-request, not cached in managed strings. `SecureString` disposed after use. |
| 11 | DB integrity | EF Core parameterized queries (no SQL injection surface); migrations applied at startup. |
| 12 | Crash safety | Domain failures via `Result<T>` (no unhandled exceptions for expected failures); global dispatcher exception handler logs and degrades gracefully. |

---

## 9. Testing Strategy (TDD: red → green → refactor)

| Project | Scope | Tooling | Mocks? |
|---|---|---|---|
| `Domain.Tests` | Aggregates, value objects, **QuestionDetector** (FIX #1), invariants, Jaccard math | xUnit + FluentAssertions | **No** — pure |
| `Application.Tests` | Handlers, behaviors, PromptBuilder, validation | xUnit + FluentAssertions + NSubstitute | Yes — ports substituted |
| `Integration.Tests` | Real SQLite (file/temp), real Whisper **tiny**, Windows OCR, repo round-trips, NetArchTest | xUnit + FluentAssertions | Real adapters |

### 9.1 Domain test — QuestionDetector (pure, no mocks)

```csharp
public class QuestionDetectorTests
{
    private readonly QuestionDetector _sut = new();

    [Theory]
    [InlineData("What is dependency injection?")]
    [InlineData("Explain the SOLID principles")]
    [InlineData("How would you optimize this query")]
    public void Evaluate_QuestionLikeText_DetectsQuestion(string text)
    {
        var result = _sut.Evaluate(text, recentQuestions: []);
        result.IsQuestion.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_StatementText_NotAQuestion()
    {
        var result = _sut.Evaluate("I have ten years of experience.", []);
        result.IsQuestion.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NearDuplicateOfRecent_MarkedDuplicate()
    {
        var recent = new[] { "What is dependency injection in dotnet?" };
        var result = _sut.Evaluate("What is dependency injection in dotnet", recent);
        result.IsDuplicate.Should().BeTrue();   // Jaccard >= 0.6
    }

    [Fact]
    public void Jaccard_IdenticalSets_ReturnsOne() =>
        QuestionDetector.Jaccard(
            new HashSet<string> { "a", "b" },
            new HashSet<string> { "a", "b" }).Should().Be(1.0);

    [Fact]
    public void Jaccard_DisjointSets_ReturnsZero() =>
        QuestionDetector.Jaccard(
            new HashSet<string> { "a" },
            new HashSet<string> { "b" }).Should().Be(0.0);
}
```

### 9.2 Domain test — Session invariants

```csharp
public class SessionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Create_WithValidInputs_ReturnsActiveSession()
    {
        var result = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now);
        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(SessionState.Active);
    }

    [Fact]
    public void AddTranscriptItem_AfterStop_Fails()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;
        session.Stop(Now);
        var item = TranscriptItem.Create(Speaker.Other, "Hello", Now, 0.9f);
        session.AddTranscriptItem(item).IsFailed.Should().BeTrue();
    }
}
```

### 9.3 Application test — handler with substituted ports

```csharp
public class StartSessionHandlerTests
{
    [Fact]
    public async Task Handle_ValidCommand_PersistsAndReturnsDto()
    {
        var repo = Substitute.For<ISessionRepository>();
        var uow = Substitute.For<IUnitOfWork>();
        uow.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Result.Ok());
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var sut = new StartSessionHandler(repo, uow, clock, new SessionMapper());

        var result = await sut.Handle(
            new StartSessionCommand(AnswerSettings.Default, CodeProfile.Empty), default);

        result.IsSuccess.Should().BeTrue();
        await repo.Received(1).AddAsync(Arg.Any<Session>(), Arg.Any<CancellationToken>());
    }
}
```

### 9.4 Application test — PromptBuilder uses ONLY CodeProfile + AnswerSettings (FIX #2)

```csharp
public class PromptBuilderServiceTests
{
    private readonly PromptBuilderService _sut = new();

    [Fact]
    public void Build_IncludesCodeProfileAndSettings_AndNoRagContext()
    {
        var profile = CodeProfile.Empty with { ProgrammingLanguage = "C#", Database = "PostgreSQL" };
        var settings = AnswerSettings.Default with { Style = AnswerStyle.CodeFirst };
        var question = DetectedQuestion.Create("Explain async/await", QuestionSource.Audio, default);

        var prompt = _sut.Build(profile, settings, question);

        prompt.System.Should().Contain("C#").And.Contain("PostgreSQL");
        prompt.System.Should().Contain("CodeFirst");
        prompt.User.Should().Contain("Explain async/await");
        // v1 contract: no knowledge-base / retrieval text injected
        prompt.System.Should().NotContain("Knowledge base");
        prompt.System.Should().NotContain("Retrieved context");
    }
}
```

### 9.5 Architecture tests (NetArchTest)

```csharp
public class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Session).Assembly;
    private static readonly Assembly Application = typeof(StartSessionCommand).Assembly;

    [Fact]
    public void Domain_ShouldNotDependOnApplicationOrInfrastructure() =>
        Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("AIHelperNET.Application", "AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

    [Fact]
    public void Application_ShouldNotDependOnInfrastructure() =>
        Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOn("AIHelperNET.Infrastructure")
            .GetResult().IsSuccessful.Should().BeTrue();

    [Fact]
    public void QuestionDetector_ShouldLiveInDomain() =>     // FIX #1 enforced
        typeof(QuestionDetector).Assembly.Should().BeSameAs(Domain);
}
```

### 9.6 Integration test — real SQLite round-trip

```csharp
public class SessionPersistenceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:").Options;
        _db = new AppDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task Session_WithTranscriptAndQuestions_RoundTrips()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is CQRS?", DateTimeOffset.UtcNow, 0.95f));
        session.AddDetectedQuestion(DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, DateTimeOffset.UtcNow));
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync();

        var reloaded = await new SessionRepository(_db).GetAsync(session.Id, default);
        reloaded.Value.Transcript.Should().HaveCount(1);
        reloaded.Value.Questions.Should().HaveCount(1);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();
}
```

---

## 10. NuGet Package List

| Package | Version | Layer | Purpose |
|---|---|---|---|
| Mediator.SourceGenerator | 3.x | Application | Zero-alloc source-generated CQRS dispatch |
| FluentResults | 3.x | Application, Infrastructure | `Result<T>` — no throwing for domain failures |
| FluentValidation | 12.x | Application | Command validation in pipeline |
| Riok.Mapperly | 4.x | Application | Source-generated DTO mapping |
| Serilog.Extensions.Hosting | 9.x | App | Structured logging host integration |
| Serilog.Sinks.File | 6.x | App | Rolling file logs |
| NAudio | 2.x | Infrastructure | WASAPI mic + loopback capture |
| Whisper.net | 1.x | Infrastructure | Local speech-to-text + VAD |
| OllamaSharp | latest | Infrastructure | Offline LLM streaming |
| Microsoft.EntityFrameworkCore.Sqlite | 10.x | Infrastructure | Session persistence |
| CommunityToolkit.Mvvm | 8.x | App | MVVM source generators (`[ObservableProperty]`, `[RelayCommand]`) |
| AdysTech.CredentialManager | latest | Infrastructure | Windows Credential Manager access |
| NetArchTest.Rules | 1.x | Integration.Tests | Architecture-rule enforcement |
| xUnit | 2.x | all tests | Test framework |
| FluentAssertions | 7.x | all tests | Assertions |
| NSubstitute | 5.x | Application.Tests | Port substitution |

Notes:
- Domain has **zero** packages (verified by NetArchTest).
- `Windows.Media.Ocr` is part of the Windows SDK (`net10.0-windows`), not a NuGet package — no Tesseract dependency.
- Claude calls use a typed `HttpClient` with raw SSE parsing (or the official Anthropic SDK if preferred); either implements `IAnswerProvider`.

---

## 11. Implementation Order (suggested)

1. Solution skeleton + `Directory.Build.props` + per-layer DI extensions + NetArchTest guard rails (red first).
2. Domain: IDs, value objects, `Session`/entities, `QuestionDetector` — full TDD in `Domain.Tests`.
3. Application: ports, Mediator + behaviors, commands/queries, `PromptBuilderService`, Mapperly — TDD in `Application.Tests`.
4. Infrastructure: EF Core + SQLite repo/UoW (integration tests), then `WindowsCredentialSecretStore`, hotkeys.
5. Infrastructure: NAudio capture → Whisper transcription pipeline (integration test with `tiny`).
6. Infrastructure: Claude + Ollama providers (contract tests against `IAnswerProvider`), Windows OCR.
7. Presentation: `IHost` startup, overlay window + stealth affinity, ViewModels, hotkey→command wiring, live streaming sink.
8. End-to-end manual verification against the §6.4 data flow; security checklist sign-off (§8).

---

## 12. Corrections from Previous Draft

1. **QuestionDetector is a pure Domain service** (`AIHelperNET.Domain.Questions.QuestionDetector`), with **no infrastructure dependencies**, registered via `AddDomain()`, and unit-tested **without mocks** in `Domain.Tests`. It is **not** in the Application layer. Enforced by an explicit NetArchTest (§9.5).
2. **PromptBuilderService (v1) composes prompts from `CodeProfile` + `AnswerSettings` only** (plus the question and optional OCR screen context). It has **no `KnowledgeBase`, no RAG, no embeddings, no retrieval**. The Application test in §9.4 asserts the absence of any retrieved/knowledge-base context. RAG is explicitly deferred to v2 as an additive enricher.
