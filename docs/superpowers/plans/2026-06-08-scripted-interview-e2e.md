# Scripted-Interview E2E (Tier A) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a deterministic, headless, automated E2E that drives the real conversation pipeline with a scripted interview through the real DI host (real Mediator, real EF/SQLite persistence, `ITurnStatusFeedback`), faking only the two AI ports, to replace the unrunnable manual system-audio gate.

**Architecture:** A test-only host boots `AddApplication()` + `AddInfrastructure()` over a shared in-memory SQLite database, overriding the AI ports (`IAnswerProviderResolver`, `IQuestionBoundaryClassifier`), the settings store, and the sinks. An `InterviewDriver` feeds an ordered script of `(Speaker, text)` segments into the real `TranscriptPipelineService.ProcessAsync`, awaiting a `CapturingAnswerStreamSink` completion signal between generating steps for determinism. Assertions read the real DB and the captured answer text.

> **DB schema note (verified against `develop`):** the project has **no EF migrations**, and the app creates its schema with `EnsureCreatedAsync()` (`App.xaml.cs:36`) — *not* `MigrateAsync()`, despite CLAUDE.md/memory. This plan uses `EnsureCreatedAsync()` (faithful to production). Coverage is of the **DI graph + persistence wiring + model→schema mapping**, not migration drift (there are no migrations). If the app later adopts migrations, switch the fixture to `MigrateAsync()`.

**Tech Stack:** .NET 10, xUnit + FluentAssertions + NSubstitute, EF Core/SQLite (`Microsoft.Data.Sqlite` in-memory shared-cache), Mediator (source-gen), `Microsoft.Extensions.DependencyInjection`.

**Spec:** `docs/superpowers/specs/2026-06-08-scripted-interview-e2e-design.md`

**Branch:** `feature/scripted-interview-e2e` (already created off `develop`, which includes the merged conversation-core work).

---

## File Structure

All new files live in `tests/AIHelperNET.Integration.Tests/E2E/`. The project already references `AIHelperNET.App`, `AIHelperNET.Application`, `AIHelperNET.Infrastructure`, and uses `Microsoft.Data.Sqlite` (see `SessionPersistenceTests`).

- `CapturingAnswerStreamSink.cs` — records streamed answer text per `(turnId, version)` and exposes a count-based completion await. The determinism linchpin.
- `FakeAnswerProvider.cs` — `IAnswerProvider` + `IAnswerProviderResolver` that returns a canned answer **echoing the prompt** so folded context is assertable.
- `FakeQuestionBoundaryClassifier.cs` — `IQuestionBoundaryClassifier` returning scripted results from a FIFO queue.
- `StubSettingsStore.cs` — `ISettingsStore` returning fixed deterministic settings (no file/secret IO).
- `InterviewHost.cs` — builds the overridden `ServiceProvider`, opens a shared in-memory SQLite connection, runs `MigrateAsync`; exposes services + fakes; `IAsyncDisposable`.
- `InterviewDriver.cs` — drives a scripted `(Speaker, text, label, expectGeneration)` timeline through `ProcessAsync`, awaiting completions.
- `ScriptedInterviewE2ETests.cs` — the two acceptance scenarios (and they own the `InterviewHost` per test for isolation).

**Decomposition rationale:** each fake has one responsibility and is independently testable; the host owns DI/DB wiring; the driver owns sequencing/quiescence; the test class owns scenarios. Keeping the singleton-stateful pipeline isolated per test (fresh host per test method) sidesteps the known cross-session state leak (follow-up #11) without depending on its fix.

---

## Task 1: `CapturingAnswerStreamSink` (the determinism linchpin)

A sink that accumulates streamed chunks per `(turnId, version)` and lets a caller await the **Nth** completion of a key (needed because a regenerated turn completes the same key twice).

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSink.cs`
- Test: `tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSinkTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSinkTests.cs`:

```csharp
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Integration.Tests.E2E;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class CapturingAnswerStreamSinkTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    [Fact]
    public async Task WaitForCompletion_CompletesAfterOnComplete_AndCapturesText()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        await sink.OnChunkAsync(turn, AnswerVersionType.Preliminary, "Hello ", default);
        await sink.OnChunkAsync(turn, AnswerVersionType.Preliminary, "world", default);
        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);

        await sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 1, Timeout);
        sink.Text(turn, AnswerVersionType.Preliminary).Should().Be("Hello world");
    }

    [Fact]
    public async Task WaitForCompletion_SecondGeneration_AwaitsSecondCompletion()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);
        await sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 1, Timeout);

        // A second await for count 2 must NOT be satisfied by the first completion.
        var pending = sink.WaitForCompletionCountAsync(turn, AnswerVersionType.Preliminary, 2, Timeout);
        pending.IsCompleted.Should().BeFalse();

        await sink.OnCompleteAsync(turn, AnswerVersionType.Preliminary, default);
        await pending; // now satisfied
    }

    [Fact]
    public async Task WaitForCompletion_TimesOut_WhenNeverCompleted()
    {
        var sink = new CapturingAnswerStreamSink();
        var turn = ConversationTurnId.New();

        var act = async () => await sink.WaitForCompletionCountAsync(
            turn, AnswerVersionType.Preliminary, 1, TimeSpan.FromMilliseconds(200));

        await act.Should().ThrowAsync<TimeoutException>();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~CapturingAnswerStreamSinkTests"`
Expected: FAIL — `CapturingAnswerStreamSink` does not exist (compile error).

- [ ] **Step 3: Implement the sink**

Create `tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSink.cs`:

```csharp
using System.Collections.Concurrent;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Test <see cref="IAnswerStreamSink"/> that accumulates streamed answer text per
/// (turn, version) and lets a driver deterministically await the Nth completion of a key.
/// A regenerated turn completes the same key more than once, so completions are counted.
/// </summary>
public sealed class CapturingAnswerStreamSink : IAnswerStreamSink
{
    private readonly record struct Key(ConversationTurnId TurnId, AnswerVersionType Version);

    private sealed class Entry
    {
        public readonly StringBuilder Text = new();
        public int CompletedCount;
        public TaskCompletionSource Pulse =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly ConcurrentDictionary<Key, Entry> _entries = new();
    private readonly ConcurrentBag<string> _errors = [];

    /// <summary>Error messages reported via <see cref="OnErrorAsync"/>, for assertions.</summary>
    public IReadOnlyCollection<string> Errors => _errors;

    private Entry GetEntry(ConversationTurnId turnId, AnswerVersionType version)
        => _entries.GetOrAdd(new Key(turnId, version), _ => new Entry());

    /// <inheritdoc/>
    public ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct)
    {
        var entry = GetEntry(turnId, versionType);
        lock (entry) entry.Text.Append(chunk);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct)
    {
        var entry = GetEntry(turnId, versionType);
        TaskCompletionSource toSignal;
        lock (entry)
        {
            entry.CompletedCount++;
            toSignal = entry.Pulse;
            entry.Pulse = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        toSignal.SetResult();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnErrorAsync(ConversationTurnId turnId, string errorMessage, CancellationToken ct)
    {
        _errors.Add(errorMessage);
        return ValueTask.CompletedTask;
    }

    /// <summary>The accumulated answer text for a (turn, version), or empty if none.</summary>
    public string Text(ConversationTurnId turnId, AnswerVersionType version)
    {
        var entry = GetEntry(turnId, version);
        lock (entry) return entry.Text.ToString();
    }

    /// <summary>
    /// Awaits until the (turn, version) key has completed at least <paramref name="target"/> times,
    /// throwing <see cref="TimeoutException"/> if that does not happen within <paramref name="timeout"/>.
    /// </summary>
    public async Task WaitForCompletionCountAsync(
        ConversationTurnId turnId, AnswerVersionType version, int target, TimeSpan timeout)
    {
        var entry = GetEntry(turnId, version);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            Task pulse;
            lock (entry)
            {
                if (entry.CompletedCount >= target) return;
                pulse = entry.Pulse.Task;
            }
            var remaining = deadline - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
                throw new TimeoutException(
                    $"Answer for turn {turnId} {version} did not reach completion #{target} in {timeout}.");
            var done = await Task.WhenAny(pulse, Task.Delay(remaining));
            if (done != pulse && DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    $"Answer for turn {turnId} {version} did not reach completion #{target} in {timeout}.");
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~CapturingAnswerStreamSinkTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSink.cs tests/AIHelperNET.Integration.Tests/E2E/CapturingAnswerStreamSinkTests.cs
git commit -m "test(e2e): add CapturingAnswerStreamSink with counted completion await"
```

---

## Task 2: The fakes (answer provider/resolver, boundary classifier, settings stub)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/FakeAnswerProvider.cs`
- Create: `tests/AIHelperNET.Integration.Tests/E2E/FakeQuestionBoundaryClassifier.cs`
- Create: `tests/AIHelperNET.Integration.Tests/E2E/StubSettingsStore.cs`
- Test: `tests/AIHelperNET.Integration.Tests/E2E/FakesTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Integration.Tests/E2E/FakesTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Integration.Tests.E2E;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class FakesTests
{
    [Fact]
    public async Task FakeAnswerProvider_EchoesPromptUser()
    {
        var provider = new FakeAnswerProvider();
        var prompt = new AnswerPrompt("sys", "Question: What is DI?", "English", 256);

        var sb = new System.Text.StringBuilder();
        await foreach (var chunk in provider.StreamAnswerAsync(prompt, default))
            sb.Append(chunk);

        sb.ToString().Should().Contain("What is DI?");
    }

    [Fact]
    public void FakeAnswerProviderResolver_AlwaysReturnsTheFake()
    {
        var provider = new FakeAnswerProvider();
        var resolver = new FakeAnswerProviderResolver(provider);
        resolver.Resolve(AiBackend.Claude).Should().BeSameAs(provider);
        resolver.Resolve(AiBackend.Ollama).Should().BeSameAs(provider);
    }

    [Fact]
    public async Task FakeBoundaryClassifier_ReturnsScriptedThenAmbiguous()
    {
        var classifier = new FakeQuestionBoundaryClassifier();
        classifier.Enqueue(new BoundaryClassificationResult(
            BoundaryLabel.NewQuestion, 0.95, true, false, true, "Q", "scripted"));

        var item = TranscriptItem.Create(Speaker.Other, "Q", DateTimeOffset.UnixEpoch, 0.9f);
        var first = await classifier.ClassifyAsync(null, new[] { item }, item, Speaker.Other, default);
        var second = await classifier.ClassifyAsync(null, new[] { item }, item, Speaker.Other, default);

        first.Classification.Should().Be(BoundaryLabel.NewQuestion);
        second.Classification.Should().Be(BoundaryLabel.NoQuestion); // Ambiguous default
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~FakesTests"`
Expected: FAIL — the fake types do not exist.

- [ ] **Step 3: Implement `FakeAnswerProvider` + resolver**

Create `tests/AIHelperNET.Integration.Tests/E2E/FakeAnswerProvider.cs`:

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Deterministic <see cref="IAnswerProvider"/> that returns a canned answer which embeds the user
/// prompt. Echoing the prompt lets a test assert that specific folded context (e.g. a clarification)
/// reached the model.
/// </summary>
public sealed class FakeAnswerProvider : IAnswerProvider
{
    /// <inheritdoc/>
    public AiBackend Backend => AiBackend.Claude;

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return "ANSWER>> ";
        await Task.Yield();
        yield return prompt.User;
    }
}

/// <summary>Resolver that always returns the supplied <see cref="FakeAnswerProvider"/>.</summary>
public sealed class FakeAnswerProviderResolver(IAnswerProvider provider) : IAnswerProviderResolver
{
    /// <inheritdoc/>
    public IAnswerProvider Resolve(AiBackend backend) => provider;
}
```

> Verify `AnswerPrompt`'s constructor parameter order/names by reading `src/AIHelperNET.Application/Answers/AnswerPrompt.cs` (used as `new AnswerPrompt(System, User, OutputLanguage, MaxTokens)` in `PromptBuilderService`). Adjust the test's `AnswerPrompt(...)` literal if the record differs.

- [ ] **Step 4: Implement `FakeQuestionBoundaryClassifier`**

Create `tests/AIHelperNET.Integration.Tests/E2E/FakeQuestionBoundaryClassifier.cs`:

```csharp
using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Deterministic <see cref="IQuestionBoundaryClassifier"/> that dequeues scripted results in FIFO
/// order. When the queue is empty it returns <see cref="BoundaryClassificationResult.Ambiguous"/>,
/// letting the heuristic decide. Note: the pipeline only consults the classifier when its heuristic
/// is below 0.7 confidence, so scripted labels route only for heuristically-ambiguous text.
/// </summary>
public sealed class FakeQuestionBoundaryClassifier : IQuestionBoundaryClassifier
{
    private readonly ConcurrentQueue<BoundaryClassificationResult> _scripted = new();

    /// <summary>Enqueues the next scripted result to be returned by <see cref="ClassifyAsync"/>.</summary>
    public void Enqueue(BoundaryClassificationResult result) => _scripted.Enqueue(result);

    /// <inheritdoc/>
    public Task<BoundaryClassificationResult> ClassifyAsync(
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<TranscriptItem> recentItems,
        TranscriptItem latestItem,
        Speaker speaker,
        CancellationToken ct)
        => Task.FromResult(_scripted.TryDequeue(out var r)
            ? r
            : BoundaryClassificationResult.Ambiguous(latestItem.Text));
}
```

- [ ] **Step 5: Implement `StubSettingsStore`**

Create `tests/AIHelperNET.Integration.Tests/E2E/StubSettingsStore.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>Deterministic <see cref="ISettingsStore"/> that avoids file/secret IO.</summary>
public sealed class StubSettingsStore : ISettingsStore
{
    private readonly AppSettingsDto _settings = new(
        ActiveBackend: AiBackend.Claude,
        WhisperModel: WhisperModelSize.Base,
        AnswerSettings: AnswerSettings.Default,
        CodeProfile: CodeProfile.Empty,
        MicDeviceId: null,
        LoopbackDeviceId: null);

    /// <inheritdoc/>
    public Task<AppSettingsDto> LoadAsync(CancellationToken ct) => Task.FromResult(_settings);

    /// <inheritdoc/>
    public Task SaveAsync(AppSettingsDto settings, CancellationToken ct) => Task.CompletedTask;
}
```

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~FakesTests"`
Expected: PASS (3 tests).

- [ ] **Step 7: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/FakeAnswerProvider.cs tests/AIHelperNET.Integration.Tests/E2E/FakeQuestionBoundaryClassifier.cs tests/AIHelperNET.Integration.Tests/E2E/StubSettingsStore.cs tests/AIHelperNET.Integration.Tests/E2E/FakesTests.cs
git commit -m "test(e2e): add fake answer provider, boundary classifier, settings stub"
```

---

## Task 3: `InterviewHost` (real DI + in-memory SQLite) with a wiring smoke test

This is the riskiest task: it boots the production DI graph, overrides the right services, swaps to in-memory SQLite, and creates the schema with `EnsureCreatedAsync()` (matching the app). The smoke test is itself a mini-E2E proving the real Mediator → real `GenerateAnswerHandler` → fake provider → real EF persistence path.

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs`
- Test: `tests/AIHelperNET.Integration.Tests/E2E/InterviewHostSmokeTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Integration.Tests/E2E/InterviewHostSmokeTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class InterviewHostSmokeTests
{
    [Fact]
    public async Task Host_Boots_CreatesSchema_AndAnswerCommandPersistsViaRealMediator()
    {
        await using var host = await InterviewHost.CreateAsync();

        // Seed a session + question + turn through the real repository.
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        var q = DetectedQuestion.Create("What is DI?", QuestionSource.Audio, DateTimeOffset.UtcNow);
        session.AddDetectedQuestion(q);
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is DI?", DateTimeOffset.UtcNow, 0.9f));
        var turn = session.AddConversationTurn(q.Id, "What is DI?", DateTimeOffset.UtcNow).Value;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await repo.AddAsync(session, default);
            await uow.SaveChangesAsync(default);
        }

        // Drive the answer command through the REAL Mediator (separate scope, like the pipeline does).
        var mediator = host.Services.GetRequiredService<IMediator>();
        var result = await mediator.Send(
            new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary), default);
        result.IsSuccess.Should().BeTrue();

        // Real EF persisted the answer version; the fake provider echoed the prompt.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
            var reloaded = (await repo.GetAsync(session.Id, default)).Value;
            var t = reloaded.ConversationTurns.Single();
            t.AnswerVersions.Should().HaveCount(1);
            t.Status.Should().Be(ConversationTurnStatus.PreliminaryReady);
        }

        // The feedback channel received the transitions.
        var feedback = host.Services.GetRequiredService<ITurnStatusFeedback>();
        var statuses = new List<ConversationTurnStatus>();
        while (feedback.TryDrain(out var e)) statuses.Add(e.Status);
        statuses.Should().Contain(ConversationTurnStatus.PreliminaryReady);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~InterviewHostSmokeTests"`
Expected: FAIL — `InterviewHost` does not exist.

- [ ] **Step 3: Implement `InterviewHost`**

Create `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs`:

```csharp
using AIHelperNET.Application;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// A headless DI host that boots the real Application + Infrastructure registrations over an
/// in-memory SQLite database (schema created via EnsureCreatedAsync, matching the app), overriding
/// only the AI ports, the settings store, and the sinks. One host per test keeps the singleton
/// pipeline state isolated.
/// </summary>
public sealed class InterviewHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly SqliteConnection _keepAlive;

    /// <summary>The resolved service provider for the headless host.</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The capturing answer sink (singleton) for awaiting completions and reading text.</summary>
    public CapturingAnswerStreamSink Sink { get; }

    /// <summary>The scripted boundary classifier (singleton) to enqueue per-Other-step results.</summary>
    public FakeQuestionBoundaryClassifier Classifier { get; }

    private InterviewHost(ServiceProvider provider, SqliteConnection keepAlive,
        CapturingAnswerStreamSink sink, FakeQuestionBoundaryClassifier classifier)
    {
        _provider = provider;
        _keepAlive = keepAlive;
        Sink = sink;
        Classifier = classifier;
    }

    /// <summary>Builds the host, opens the shared in-memory DB, and applies EF migrations.</summary>
    public static async Task<InterviewHost> CreateAsync()
    {
        // A uniquely-named shared-cache in-memory DB so each test/host is isolated yet visible
        // across all DI scopes/connections for the host's lifetime.
        var dbName = "interview-e2e-" + Guid.NewGuid().ToString("N");
        var connString = $"Data Source={dbName};Mode=Memory;Cache=Shared";
        var keepAlive = new SqliteConnection(connString);
        await keepAlive.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);

        IConfiguration config = new ConfigurationBuilder().Build();
        services.AddApplication();
        services.AddInfrastructure(config);

        // ── Override AppDbContext to the shared in-memory connection ────────────
        RemoveDbContext(services);
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connString));

        // ── Override the AI ports + settings store (last AddSingleton wins for resolution) ──
        var fakeProvider = new FakeAnswerProvider();
        services.AddSingleton<IAnswerProviderResolver>(new FakeAnswerProviderResolver(fakeProvider));
        var classifier = new FakeQuestionBoundaryClassifier();
        services.AddSingleton<IQuestionBoundaryClassifier>(classifier);
        services.AddSingleton<ISettingsStore, StubSettingsStore>();

        // ── Register the sinks the pipeline + handler need (not provided by AddInfrastructure) ──
        var sink = new CapturingAnswerStreamSink();
        services.AddSingleton<IAnswerStreamSink>(sink);
        services.AddSingleton<ITranscriptSink>(Substitute.For<ITranscriptSink>());
        services.AddSingleton<IConversationTurnSink>(Substitute.For<IConversationTurnSink>());

        var provider = services.BuildServiceProvider();

        // ── Create the schema on the in-memory DB (matches the app's EnsureCreatedAsync) ──
        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        return new InterviewHost(provider, keepAlive, sink, classifier);
    }

    private static void RemoveDbContext(IServiceCollection services)
    {
        var toRemove = services.Where(d =>
            d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
            d.ServiceType == typeof(DbContextOptions) ||
            d.ServiceType == typeof(AppDbContext)).ToList();
        foreach (var d in toRemove) services.Remove(d);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _keepAlive.DisposeAsync();
    }
}
```

> **Implementation checks during this task:**
> - The project has **no EF migrations** and the app uses `EnsureCreatedAsync()` — so the fixture uses it too (already in the code above). Do **not** use `MigrateAsync()` (it would no-op and leave no schema). This is verified against `develop` (`App.xaml.cs:36`).
> - Confirm no `AddInfrastructure` registration runs eager work that breaks headless `BuildServiceProvider()`/resolution. Registration is lazy; only services resolved by the host are constructed (Mediator handler graph, `AppDbContext`, repo, uow, pipeline, settings/provider/classifier/sinks, `TimeProvider`). Audio/Whisper/OCR are never resolved. `AppPaths.EnsureDirectoriesExist()` runs at `AddInfrastructure` time and only creates data directories — harmless.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~InterviewHostSmokeTests"`
Expected: PASS. If it fails on DI resolution, read the exception: a missing `ITranscriptSink`/`IConversationTurnSink`/`IAnswerStreamSink`/`TimeProvider` registration is the likely cause — all are registered above; adjust only if the pipeline/handler constructor surface differs.

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs tests/AIHelperNET.Integration.Tests/E2E/InterviewHostSmokeTests.cs
git commit -m "test(e2e): add InterviewHost (real DI + in-memory SQLite) with wiring smoke test"
```

---

## Task 4: `InterviewDriver` + Scenario 1 (parallel answered cards)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/InterviewDriver.cs`
- Test: `tests/AIHelperNET.Integration.Tests/E2E/ScriptedInterviewE2ETests.cs`

- [ ] **Step 1: Write the failing test (Scenario 1)**

Create `tests/AIHelperNET.Integration.Tests/E2E/ScriptedInterviewE2ETests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class ScriptedInterviewE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    private static BoundaryClassificationResult AdditionalRequirement(string text) =>
        new(BoundaryLabel.AdditionalRequirement, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: true, ShouldCreateNewTurn: false,
            NormalizedQuestionText: text, Reason: "scripted");

    [Fact]
    public async Task Scenario1_TwoOtherQuestions_ProduceTwoAnsweredCards()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;

        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISessionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var pipeline = sp.GetRequiredService<TranscriptPipelineService>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);

        var driver = new InterviewDriver(pipeline, uow, _host.Sink, _host.Classifier);

        await driver.OtherAsync(session, "What is dependency injection in one sentence?",
            NewQuestion("What is dependency injection?"));
        await driver.OtherAsync(session, "Now explain CQRS in one sentence?",
            NewQuestion("Now explain CQRS?"));

        // Reload from the DB in a fresh scope and assert both cards have answers.
        await using var verifyScope = _host.Services.CreateAsyncScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var reloaded = (await verifyRepo.GetAsync(session.Id, default)).Value;

        reloaded.ConversationTurns.Should().HaveCount(2);
        reloaded.ConversationTurns.Should().OnlyContain(t => t.AnswerVersions.Count >= 1);
        _host.Sink.Errors.Should().BeEmpty();
    }
}
```

> The `Other` texts are phrased as questions so the heuristic recognises them as complete questions; the scripted `NewQuestion` result is the deterministic backstop if the heuristic is low-confidence. During implementation, run the test and check the `BoundaryRoute` log / behaviour: both segments must create a turn and fire generation. If the heuristic mis-routes a segment (e.g. treats the second as a continuation), adjust the segment text to be unambiguously a new question (the assertion — two answered turns — is the contract).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~Scenario1_TwoOtherQuestions"`
Expected: FAIL — `InterviewDriver` does not exist.

- [ ] **Step 3: Implement `InterviewDriver`**

Create `tests/AIHelperNET.Integration.Tests/E2E/InterviewDriver.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Drives a scripted interview through the real <see cref="TranscriptPipelineService"/>, feeding
/// ordered segments and deterministically awaiting answer completion between generating steps.
/// </summary>
public sealed class InterviewDriver(
    TranscriptPipelineService pipeline,
    IUnitOfWork unitOfWork,
    CapturingAnswerStreamSink sink,
    FakeQuestionBoundaryClassifier classifier)
{
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(10);
    private readonly Dictionary<ConversationTurnId, int> _preliminaryCount = new();
    private DateTimeOffset _clock = DateTimeOffset.UnixEpoch;

    private DateTimeOffset NextTimestamp() => _clock = _clock.AddSeconds(1);

    /// <summary>
    /// Feeds an interviewer (<see cref="Speaker.Other"/>) segment with a scripted boundary result,
    /// then awaits the resulting (re)generation to complete.
    /// </summary>
    public async Task OtherAsync(Session session, string text, BoundaryClassificationResult scripted)
    {
        classifier.Enqueue(scripted);
        await pipeline.ProcessAsync(
            session, TranscriptItem.Create(Speaker.Other, text, NextTimestamp(), 0.95f),
            unitOfWork, CancellationToken.None);

        // The fired generation targets the most-recently-active turn at Preliminary.
        var turnId = session.ConversationTurns[^1].Id;
        var target = _preliminaryCount.GetValueOrDefault(turnId) + 1;
        _preliminaryCount[turnId] = target;
        await sink.WaitForCompletionCountAsync(
            turnId, AnswerVersionType.Preliminary, target, AnswerTimeout);
    }

    /// <summary>
    /// Feeds a candidate (<see cref="Speaker.Me"/>) segment. Per the conversation model this never
    /// generates, so there is nothing to await.
    /// </summary>
    public async Task MeAsync(Session session, string text)
        => await pipeline.ProcessAsync(
            session, TranscriptItem.Create(Speaker.Me, text, NextTimestamp(), 0.95f),
            unitOfWork, CancellationToken.None);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~Scenario1_TwoOtherQuestions"`
Expected: PASS. If the second segment does not create a second turn (heuristic routed it as a continuation/clarification), revise its text to be an unambiguous standalone question and re-run.

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/InterviewDriver.cs tests/AIHelperNET.Integration.Tests/E2E/ScriptedInterviewE2ETests.cs
git commit -m "test(e2e): scenario 1 — two Other questions produce two answered cards"
```

---

## Task 5: Scenario 2 (clarification incorporated into regeneration)

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/ScriptedInterviewE2ETests.cs`

- [ ] **Step 1: Write the failing test (add Scenario 2)**

Add this `[Fact]` to `ScriptedInterviewE2ETests`:

```csharp
    [Fact]
    public async Task Scenario2_MeClarification_IsIncorporatedIntoRegeneratedAnswer()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;

        await using var scope = _host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var repo = sp.GetRequiredService<ISessionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var pipeline = sp.GetRequiredService<TranscriptPipelineService>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);

        var driver = new InterviewDriver(pipeline, uow, _host.Sink, _host.Classifier);

        // 1) Interviewer asks; we wait for the preliminary answer.
        await driver.OtherAsync(session, "What is dependency injection in one sentence?",
            NewQuestion("What is dependency injection?"));

        // 2) Candidate clarifies — deterministic Me path: attaches context, no generation.
        await driver.MeAsync(session, "do you mean constructor injection specifically?");

        // 3) Interviewer responds; scripted AdditionalRequirement → Rule 8 regeneration of the same turn.
        await driver.OtherAsync(session, "yes exactly, that flavor",
            AdditionalRequirement("yes exactly"));

        var turnId = session.ConversationTurns.Single().Id;

        // The latest answer text echoes the folded clarification (FakeAnswerProvider echoes the prompt).
        _host.Sink.Text(turnId, AnswerVersionType.Preliminary)
            .Should().Contain("constructor injection specifically");

        // And the DB shows the turn was regenerated (>= 2 answer versions).
        await using var verifyScope = _host.Services.CreateAsyncScope();
        var verifyRepo = verifyScope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var reloaded = (await verifyRepo.GetAsync(session.Id, default)).Value;
        reloaded.ConversationTurns.Single().AnswerVersions.Count.Should().BeGreaterThanOrEqualTo(2);
        _host.Sink.Errors.Should().BeEmpty();
    }
```

> Determinism notes for this scenario:
> - Step 2's `Me` segment routes via the deterministic `HandleMeUtterance` (no classifier enqueue needed). Because step 1 awaited completion, the pipeline's next `ProcessAsync` (this `Me` step) drains the `PreliminaryReady` feedback first, so the turn is answered when `Me` arrives → context-only attach (no status change).
> - Step 3's text "yes exactly, that flavor" is deliberately **not** an interrogative, so the heuristic is low-confidence and the scripted `AdditionalRequirement` result is what routes — regenerating the existing turn (Rule 8, now alive because the feedback drain set the in-memory status to `PreliminaryReady`).
> - The `Sink.Text` for `(turnId, Preliminary)` accumulates BOTH generations' chunks; both echo the prompt, and the regeneration's prompt contains the clarification, so the assertion holds. If you want to assert only the second generation's text, clear is not necessary — the substring is present either way.

- [ ] **Step 2: Run test to verify it fails (or passes)**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~Scenario2_MeClarification"`
Expected: This should PASS once the implementation from Tasks 1–4 is in place (no new production code is needed — it exercises existing behaviour). If it FAILS because the regenerated answer does not contain the clarification text, that indicates a real defect in clarification-folding or feedback-drain ordering — investigate per the spec §5/§6 rather than weakening the assertion. If step 3 creates a *new* turn instead of regenerating (heuristic routed "yes exactly, that flavor" as a new question), make the text more clearly a non-question acknowledgement and re-run.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/ScriptedInterviewE2ETests.cs
git commit -m "test(e2e): scenario 2 — Me clarification incorporated into regenerated answer"
```

---

## Task 6: Full verification + branch wrap-up

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings, 0 errors (`TreatWarningsAsErrors`).

- [ ] **Step 2: Run the full Integration suite**

Run: `dotnet test tests/AIHelperNET.Integration.Tests`
Expected: All green, including the new `CapturingAnswerStreamSinkTests`, `FakesTests`, `InterviewHostSmokeTests`, and `ScriptedInterviewE2ETests` (both scenarios), plus the pre-existing persistence/architecture/session-runner tests.

- [ ] **Step 3: Run the entire suite**

Run: `dotnet test`
Expected: Domain/Application/Infrastructure/Integration green. (UITests may show the 2 known pre-existing failures `BothMode_MicAndSystemDotsActive` and `ScreenCaptureTests.Capture_WithTestImage_ProducesTurnCard`, unrelated to this work.)

- [ ] **Step 4: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to open a PR from `feature/scripted-interview-e2e` → `develop`. PR summary: a deterministic Tier-A E2E that replaces the manual system-audio gate for conversation-core regressions; note that it exercises the real DI graph + EF migrations and runs in CI in seconds.

---

## Self-Review notes (for the executor)

- **Spec coverage:** §3 host approach → Task 3 (`InterviewHost`); §4.1 fixture → Task 3; §4.2 fake provider (echo) → Task 2 + Scenario 2 assertion; §4.3 fake classifier (scripted, heuristic caveat) → Task 2 + the per-`Other` text notes in Tasks 4/5; §4.4 capturing sink → Task 1; §4.5 driver → Task 4; §5 quiescence (counted completion await) → Task 1 + driver; §6 scenarios → Tasks 4/5; §7 error handling (bounded timeout, `Errors`) → Task 1; §8 testing → all tasks.
- **Out of scope (do not add):** real audio, Whisper, OCR, `SessionRunner`, model accuracy; no production code changes are required by this plan (it is test-only). If a scenario reveals a *production* defect, stop and report — do not weaken assertions.
- **Type/signature consistency:** sink key is `(ConversationTurnId, AnswerVersionType)`; the await method is `WaitForCompletionCountAsync(turnId, version, target, timeout)`; driver methods are `OtherAsync(session, text, BoundaryClassificationResult)` and `MeAsync(session, text)`; host factory is `InterviewHost.CreateAsync()` exposing `Services`, `Sink`, `Classifier`. `BoundaryClassificationResult` positional args are `(Classification, Confidence, ShouldGenerateAnswer, ShouldRefineExistingAnswer, ShouldCreateNewTurn, NormalizedQuestionText, Reason)`. `AppSettingsDto` is `(ActiveBackend, WhisperModel, AnswerSettings, CodeProfile, MicDeviceId, LoopbackDeviceId, ...)`.
- **Determinism caveats are documented at the call sites** (Tasks 4/5 notes) because they depend on the real heuristic; the executor must verify the intended route fires and adjust scripted text if needed, never weaken the assertions.
- **Known interaction:** the plan relies on the handler's publish-before-`OnComplete` ordering (spec §5). It is currently true (`GenerateAnswerCommand.cs`); if a future change reorders it, Scenario 2 becomes racy.
