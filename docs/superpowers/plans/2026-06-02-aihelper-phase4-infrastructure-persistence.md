# AIHelperNET — Phase 4: Infrastructure – Persistence, Security, Hotkeys

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 3 complete (Application layer green).

**Goal:** Implement all infrastructure adapters that don't require audio hardware: EF Core + SQLite persistence, secret store (Windows Credential Manager), global hotkeys, AppPaths, JsonSettingsStore, and the `GetAudioDevicesQuery` handler. Add integration tests for persistence.

**Architecture:** All classes implement ports declared in Application. Target `net10.0-windows`. No ViewModel or WPF references.

**Tech Stack:** EF Core Sqlite 10.x, AdysTech.CredentialManager, NAudio (device enumeration only in this phase), xUnit + FluentAssertions

---

### Task 20: AppPaths

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Common/AppPaths.cs`

- [ ] **Step 1: Create AppPaths**

```csharp
// src/AIHelperNET.Infrastructure/Common/AppPaths.cs
namespace AIHelperNET.Infrastructure.Common;

public static class AppPaths
{
    private static readonly string Base =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHelperNET");

    public static string DatabaseFile  => Path.Combine(Base, "sessions.db");
    public static string SettingsFile  => Path.Combine(Base, "settings.json");
    public static string LogDirectory  => Path.Combine(Base, "logs");
    public static string LogFile       => Path.Combine(LogDirectory, "log-.txt");
    public static string ModelsDir     => Path.Combine(Base, "models");

    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Base);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ModelsDir);
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Common/AppPaths.cs
git commit -m "feat(infra): add AppPaths"
```

---

### Task 21: EF Core — AppDbContext and entity configuration

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create AppDbContext**

```csharp
// src/AIHelperNET.Infrastructure/Persistence/AppDbContext.cs
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Session> Sessions => Set<Session>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        var s = b.Entity<Session>();

        s.HasKey(x => x.Id);
        s.Property(x => x.Id)
            .HasConversion(id => id.Value, v => new SessionId(v));

        s.OwnsOne(x => x.AnswerSettings, a =>
        {
            a.Property(p => p.OutputLanguage).HasMaxLength(64);
        });
        s.OwnsOne(x => x.CodeProfile);

        s.Navigation(x => x.Transcript).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Questions).UsePropertyAccessMode(PropertyAccessMode.Field);
        s.Navigation(x => x.Answers).UsePropertyAccessMode(PropertyAccessMode.Field);

        s.OwnsMany(x => x.Transcript, t =>
        {
            t.HasKey(x => x.Id);
            t.Property(x => x.Id).HasConversion(id => id.Value, v => new TranscriptItemId(v));
            t.Property(x => x.Text).HasMaxLength(4000);
        });

        s.OwnsMany(x => x.Questions, q =>
        {
            q.HasKey(x => x.Id);
            q.Property(x => x.Id).HasConversion(id => id.Value, v => new QuestionId(v));
            q.Property(x => x.Text).HasMaxLength(2000);
        });

        s.OwnsMany(x => x.Answers, a =>
        {
            a.HasKey(x => x.Id);
            a.Property(x => x.Id).HasConversion(id => id.Value, v => new AnswerId(v));
            a.Property(x => x.QuestionId).HasConversion(id => id.Value, v => new QuestionId(v));
            // Content is not persisted via EF — it lives in the in-memory StringBuilder during streaming.
            // After streaming completes, Content is accessed via GeneratedAnswer.Content property.
            // EF stores a snapshot via a backing field — add a shadow property:
            a.Property<string>("_contentSnapshot").HasColumnName("Content").HasMaxLength(32000);
        });
    }
}
```

Note on `GeneratedAnswer.Content`: EF Core cannot map computed properties backed by `StringBuilder`. Add a shadow property `_contentSnapshot` and map it, then wire a `SaveChanges` intercept or configure the entity to copy content on save. Simpler approach: make `Content` a regular auto-property set internally, keeping `_buffer` only for streaming.

Update `GeneratedAnswer` in Domain to support EF persistence (add backing field workaround):

Edit `src/AIHelperNET.Domain/Sessions/GeneratedAnswer.cs` — add a persistent content field:

```csharp
// src/AIHelperNET.Domain/Sessions/GeneratedAnswer.cs
using System.Text;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

public sealed class GeneratedAnswer
{
    private readonly StringBuilder _buffer = new();
    private string _persistedContent = string.Empty;

    public AnswerId Id { get; }
    public QuestionId QuestionId { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public AnswerStatus Status { get; private set; }

    // Returns live buffer during streaming, persisted snapshot after completion
    public string Content => _buffer.Length > 0 ? _buffer.ToString() : _persistedContent;

    private GeneratedAnswer(AnswerId id, QuestionId questionId, DateTimeOffset at)
        => (Id, QuestionId, StartedAt, Status) = (id, questionId, at, AnswerStatus.Streaming);

    public static GeneratedAnswer Create(QuestionId questionId, DateTimeOffset at)
        => new(AnswerId.New(), questionId, at);

    public void AppendChunk(string chunk) => _buffer.Append(chunk);

    public void Complete(DateTimeOffset at)
    {
        Status = AnswerStatus.Completed;
        CompletedAt = at;
        _persistedContent = _buffer.ToString();
        _buffer.Clear();
    }

    public void Cancel(DateTimeOffset at)
    {
        if (Status == AnswerStatus.Streaming) Status = AnswerStatus.Cancelled;
        _persistedContent = _buffer.ToString();
        _buffer.Clear();
        CompletedAt = at;
    }

    public void Fail(DateTimeOffset at)
    {
        Status = AnswerStatus.Failed;
        _persistedContent = _buffer.ToString();
        _buffer.Clear();
        CompletedAt = at;
    }

    private GeneratedAnswer() { } // EF Core
}
```

Update AppDbContext to use `_persistedContent` backing field:

```csharp
// In the OwnsMany(x => x.Answers, ...) block:
a.Property(x => x.Content)
    .HasField("_persistedContent")
    .UsePropertyAccessMode(PropertyAccessMode.Field)
    .HasColumnName("Content")
    .HasMaxLength(32000);
```

Remove the shadow property approach. Final OwnsMany for Answers:

```csharp
s.OwnsMany(x => x.Answers, a =>
{
    a.HasKey(x => x.Id);
    a.Property(x => x.Id).HasConversion(id => id.Value, v => new AnswerId(v));
    a.Property(x => x.QuestionId).HasConversion(id => id.Value, v => new QuestionId(v));
    a.Property(x => x.Content)
        .HasField("_persistedContent")
        .UsePropertyAccessMode(PropertyAccessMode.Field)
        .HasColumnName("Content")
        .HasMaxLength(32000);
});
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Domain/Sessions/GeneratedAnswer.cs src/AIHelperNET.Infrastructure/Persistence/AppDbContext.cs
git commit -m "feat(infra): add AppDbContext with EF Core entity configuration"
```

---

### Task 22: SessionRepository and EfUnitOfWork

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Persistence/SessionRepository.cs`
- Create: `src/AIHelperNET.Infrastructure/Persistence/EfUnitOfWork.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/SessionPersistenceTests.cs`

- [ ] **Step 1: Write failing integration tests**

```csharp
// tests/AIHelperNET.Integration.Tests/Persistence/SessionPersistenceTests.cs
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIHelperNET.Integration.Tests.Persistence;

public class SessionPersistenceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;
    private SessionRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _repo = new SessionRepository(_db);
    }

    [Fact]
    public async Task AddAndGet_RoundTripsSession()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        await _repo.AddAsync(session, default);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var result = await _repo.GetAsync(session.Id, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(session.Id);
        result.Value.State.Should().Be(SessionState.Active);
    }

    [Fact]
    public async Task AddAndGet_WithTranscriptAndQuestions_Roundtrips()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is CQRS?", DateTimeOffset.UtcNow, 0.95f));
        var q = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, DateTimeOffset.UtcNow);
        session.AddDetectedQuestion(q);

        await _repo.AddAsync(session, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _repo.GetAsync(session.Id, default);
        loaded.Value.Transcript.Should().HaveCount(1);
        loaded.Value.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetHistory_ReturnsOrderedSummaries()
    {
        for (int i = 0; i < 3; i++)
        {
            var s = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow.AddMinutes(i)).Value;
            await _repo.AddAsync(s, default);
        }
        await _db.SaveChangesAsync();

        var history = await _repo.GetHistoryAsync(10, default);
        history.Should().HaveCount(3);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();
}
```

- [ ] **Step 2: Run — compile error (SessionRepository not found)**

```powershell
dotnet test tests/AIHelperNET.Integration.Tests/ --logger "console;verbosity=minimal"
```

- [ ] **Step 3: Implement SessionRepository**

```csharp
// src/AIHelperNET.Infrastructure/Persistence/SessionRepository.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class SessionRepository(AppDbContext db) : ISessionRepository
{
    public async Task<Result<Session>> GetAsync(SessionId id, CancellationToken ct)
    {
        var session = await db.Sessions
            .Include("_transcript")
            .Include("_questions")
            .Include("_answers")
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return session is null
            ? Result.Fail($"Session {id.Value} not found.")
            : Result.Ok(session);
    }

    public async Task AddAsync(Session session, CancellationToken ct)
        => await db.Sessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<SessionSummaryDto>> GetHistoryAsync(int take, CancellationToken ct)
    {
        return await db.Sessions
            .OrderByDescending(s => s.StartedAt)
            .Take(take)
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.StartedAt,
                s.EndedAt,
                s.State,
                s.Questions.Count,
                s.Answers.Count))
            .ToListAsync(ct);
    }

    public void Update(Session session)
        => db.Sessions.Update(session);
}
```

Note: EF Core navigation properties for owned collections are accessed by their collection field names. If EF doesn't find them via `Include("_transcript")`, use the navigation property names declared in `OnModelCreating`. Adjust if needed — EF may require `Include(nameof(Session.Transcript))` depending on how the navigation is configured.

- [ ] **Step 4: Implement EfUnitOfWork**

```csharp
// src/AIHelperNET.Infrastructure/Persistence/EfUnitOfWork.cs
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (DbUpdateException ex)
        {
            return Result.Fail(new Error("Persistence failed").CausedBy(ex));
        }
    }
}
```

- [ ] **Step 5: Run integration tests**

```powershell
dotnet test tests/AIHelperNET.Integration.Tests/ --filter "FullyQualifiedName~Persistence" --logger "console;verbosity=normal"
```

Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Persistence/ tests/AIHelperNET.Integration.Tests/Persistence/
git commit -m "feat(infra): add SessionRepository, EfUnitOfWork, and persistence integration tests"
```

---

### Task 23: JsonSettingsStore

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`

- [ ] **Step 1: Implement JsonSettingsStore**

```csharp
// src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Infrastructure.Common;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<AppSettingsDto> LoadAsync(CancellationToken ct)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return DefaultSettings();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppSettingsDto>(stream, Options, ct)
               ?? DefaultSettings();
    }

    public async Task SaveAsync(AppSettingsDto settings, CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();
        await using var stream = File.Create(AppPaths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
    }

    private static AppSettingsDto DefaultSettings() => new(
        AiBackend.Claude,
        WhisperModelSize.Base,
        Domain.ValueObjects.AnswerSettings.Default,
        Domain.ValueObjects.CodeProfile.Empty,
        null,
        null);
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs
git commit -m "feat(infra): add JsonSettingsStore"
```

---

### Task 24: WindowsCredentialSecretStore

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Security/WindowsCredentialSecretStore.cs`

- [ ] **Step 1: Implement**

```csharp
// src/AIHelperNET.Infrastructure/Security/WindowsCredentialSecretStore.cs
using System.Net;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AdysTech.CredentialManager;
using FluentResults;

namespace AIHelperNET.Infrastructure.Security;

public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string Target = "AIHelperNET:ClaudeApiKey";

    public Result SaveApiKey(SecureString key)
    {
        try
        {
            CredentialManager.SaveCredentials(Target,
                new NetworkCredential(string.Empty, key));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not save API key").CausedBy(ex));
        }
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
        try
        {
            CredentialManager.RemoveCredentials(Target);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not delete API key").CausedBy(ex));
        }
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Security/
git commit -m "feat(infra): add WindowsCredentialSecretStore"
```

---

### Task 25: GlobalHotkeyService

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Hotkeys/GlobalHotkeyService.cs`

- [ ] **Step 1: Implement**

```csharp
// src/AIHelperNET.Infrastructure/Hotkeys/GlobalHotkeyService.cs
using System.Runtime.InteropServices;
using System.Windows.Interop;
using AIHelperNET.Application.Abstractions;
using FluentResults;

namespace AIHelperNET.Infrastructure.Hotkeys;

public sealed class GlobalHotkeyService : IGlobalHotkeyService, IDisposable
{
    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;

    private HwndSource? _source;
    private readonly List<HotkeyId> _registered = [];

    public event EventHandler<HotkeyId>? HotkeyPressed;

    public void Initialize(IntPtr parentHwnd)
    {
        _source = HwndSource.FromHwnd(parentHwnd);
        _source?.AddHook(WndProc);
    }

    public Result Register(HotkeyId id, ModifierKeys modifiers, VirtualKey key)
    {
        if (_source is null)
            return Result.Fail("HotkeyService not initialized — call Initialize(hwnd) first.");

        var ok = RegisterHotKey(_source.Handle, (int)id, (uint)modifiers, (uint)key);
        if (ok) _registered.Add(id);
        return ok
            ? Result.Ok()
            : Result.Fail($"Failed to register hotkey {id} (already in use by another app?).");
    }

    public void UnregisterAll()
    {
        if (_source is null) return;
        foreach (var id in _registered)
            UnregisterHotKey(_source.Handle, (int)id);
        _registered.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            HotkeyPressed?.Invoke(this, (HotkeyId)wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source?.RemoveHook(WndProc);
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Hotkeys/
git commit -m "feat(infra): add GlobalHotkeyService"
```

---

### Task 26: AudioDeviceEnumerator + GetAudioDevicesQuery handler

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Audio/AudioDeviceEnumerator.cs`

- [ ] **Step 1: Implement device enumeration + query handler**

```csharp
// src/AIHelperNET.Infrastructure/Audio/AudioDeviceEnumerator.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using FluentResults;
using Mediator;
using NAudio.CoreAudioApi;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class GetAudioDevicesHandler : IRequestHandler<GetAudioDevicesQuery, Result<IReadOnlyList<AudioDeviceDto>>>
{
    public ValueTask<Result<IReadOnlyList<AudioDeviceDto>>> Handle(
        GetAudioDevicesQuery query, CancellationToken ct)
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator
            .EnumerateAudioEndPoints(DataFlow.All, DeviceState.Active)
            .Select(d => new AudioDeviceDto(d.ID, d.FriendlyName, false))
            .ToList();

        return ValueTask.FromResult(Result.Ok<IReadOnlyList<AudioDeviceDto>>(devices));
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Audio/AudioDeviceEnumerator.cs
git commit -m "feat(infra): add GetAudioDevicesHandler using NAudio MMDeviceEnumerator"
```

---

### Task 27: Infrastructure DependencyInjection (persistence + security + hotkeys)

**Files:**
- Create: `src/AIHelperNET.Infrastructure/DependencyInjection.cs`

Full DI including audio/transcription/AI stubs registered here; implementations added in Phase 5-6.

- [ ] **Step 1: Create DI extension**

```csharp
// src/AIHelperNET.Infrastructure/DependencyInjection.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Audio;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using AIHelperNET.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        AppPaths.EnsureDirectoriesExist();

        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={AppPaths.DatabaseFile}"));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // Audio/Transcription/AI/OCR registered in later phases — add registrations as each is implemented.

        return services;
    }
}
```

- [ ] **Step 2: Build entire solution**

```powershell
dotnet build AIHelperNET.sln
```

- [ ] **Step 3: Run all tests**

```powershell
dotnet test AIHelperNET.sln --logger "console;verbosity=minimal"
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs
git commit -m "feat(infra): add Infrastructure DI extension"
```

---

**Phase 4 complete.** Continue with `2026-06-02-aihelper-phase5-audio-transcription.md`.
