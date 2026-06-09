# Audio-Session E2E (Tier B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add deterministic, CI-runnable integration tests that exercise `SessionRunner`'s audio-session orchestration (frame fan-out → transcription → 300 ms same-speaker merge → speaker-change flush → pipeline) — the layer the Tier A scripted E2E skips.

**Architecture:** Reuse the Tier A `InterviewHost` (real Application+Infrastructure over in-memory SQLite, AI/settings/sink fakes). Construct a real `SessionRunner` directly with two new scripted fakes for `IAudioCaptureService` / `ITranscriptionService`; the DI-registered NAudio/Whisper services go unused. These are characterization tests of existing orchestration — each scenario is expected to PASS green (a failure means a real bug). One small production change makes the merge window injectable for fast, deterministic timing.

**Tech Stack:** C# / .NET 10, xUnit, FluentAssertions, NSubstitute, EF Core + in-memory SQLite, `IAsyncEnumerable`/`System.Threading.Channels`.

**Spec:** `docs/superpowers/specs/2026-06-09-audio-session-e2e-design.md`

---

### Task 1: Make `SessionRunner`'s merge window injectable (production change)

**Files:**
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`

- [ ] **Step 1: Add a constructor parameter with the current default**

Change the primary constructor to add `int segmentMergeWindowMs = 300`:

```csharp
public sealed class SessionRunner(
    IServiceScopeFactory scopeFactory,
    IAudioCaptureService audioCapture,
    ITranscriptionService transcription,
    TranscriptPipelineService pipeline,
    int segmentMergeWindowMs = 300)
{
```

- [ ] **Step 2: Use the parameter instead of the local const**

In `RunAsync`, delete the `const int SegmentMergeWindowMs = 300;` line (keep the explanatory comment above it) and change the consumer's window line from `window.CancelAfter(SegmentMergeWindowMs);` to:

```csharp
window.CancelAfter(segmentMergeWindowMs);
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`. (MS DI uses the default value for the unresolved `int`, so `AddSingleton<SessionRunner>()` is unchanged.)

- [ ] **Step 4: Commit**

```bash
git add src/AIHelperNET.App/Services/SessionRunner.cs
git commit -m "refactor(app): make SessionRunner merge window injectable for tests"
```

---

### Task 2: Scripted audio + transcription test doubles

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs`

- [ ] **Step 1: Write the doubles**

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>One scripted spoken utterance. <paramref name="GapMsBefore"/> is the real delay emitted
/// before this utterance's frame, used to drive the segment-merge timing in SessionRunner.</summary>
public sealed record ScriptedUtterance(Speaker Speaker, string Text, int GapMsBefore);

/// <summary>Test <see cref="IAudioCaptureService"/> that replays a scripted utterance list as exactly
/// one <see cref="AudioFrame"/> each (utterance index encoded in <c>Samples[0]</c> for correlation),
/// then completes the stream so the SessionRunner loop terminates on its own.</summary>
public sealed class ScriptedAudioCaptureService(IReadOnlyList<ScriptedUtterance> script) : IAudioCaptureService
{
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection, [EnumeratorCancellation] CancellationToken ct)
    {
        var clock = DateTimeOffset.UnixEpoch;
        for (var i = 0; i < script.Count; i++)
        {
            if (script[i].GapMsBefore > 0)
                await Task.Delay(script[i].GapMsBefore, ct);
            clock = clock.AddSeconds(1);
            yield return new AudioFrame([(float)i], script[i].Speaker, clock);
        }
    }
}

/// <summary>Test <see cref="ITranscriptionService"/> that maps each <see cref="AudioFrame"/> to the
/// scripted <see cref="TranscriptSegment"/> identified by the index in <c>Samples[0]</c>. The script
/// is read-only, so concurrent mic/loopback invocations need no synchronization.</summary>
public sealed class ScriptedTranscriptionService(IReadOnlyList<ScriptedUtterance> script) : ITranscriptionService
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize model, string language,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in frames.WithCancellation(ct))
        {
            var utt = script[(int)frame.Samples[0]];
            yield return new TranscriptSegment(utt.Text, frame.Speaker, frame.CapturedAt, 0.95f);
        }
    }
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs
git commit -m "test(e2e): scripted audio capture + transcription doubles"
```

---

### Task 3: Test class scaffold + Scenario 1 (happy path)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs`

- [ ] **Step 1: Write the class, helpers, and the happy-path test**

```csharp
using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

public class SessionRunnerAudioE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    private const int MergeWindowMs = 120;
    private static readonly TimeSpan StateTimeout = TimeSpan.FromSeconds(15);

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    private async Task<Session> PersistNewSessionAsync()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        await using var scope = _host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);
        return session;
    }

    private SessionRunner NewRunner(IReadOnlyList<ScriptedUtterance> script) =>
        new(_host.Services.GetRequiredService<IServiceScopeFactory>(),
            new ScriptedAudioCaptureService(script),
            new ScriptedTranscriptionService(script),
            _host.Services.GetRequiredService<TranscriptPipelineService>(),
            segmentMergeWindowMs: MergeWindowMs);

    private async Task<Session> ReloadAsync(SessionId id)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        return (await repo.GetAsync(id, default)).Value;
    }

    /// <summary>Polls the DB until <paramref name="predicate"/> holds for the reloaded session.</summary>
    private async Task WaitForStateAsync(SessionId id, Func<Session, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (predicate(await ReloadAsync(id))) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Session {id} did not reach the expected state in {timeout}.");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task SingleOtherUtterance_ProducesOneAnsweredTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[] { new ScriptedUtterance(Speaker.Other, "What is dependency injection?", 0) };
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, new AudioDeviceSelection("mic", "loopback"),
            WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await WaitForStateAsync(session.Id,
            s => s.ConversationTurns.Count >= 1 && s.ConversationTurns.All(t => t.AnswerVersions.Count >= 1),
            StateTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle()
            .Which.AnswerVersions.Count.Should().BeGreaterThanOrEqualTo(1);
        _host.Sink.Errors.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~SingleOtherUtterance_ProducesOneAnsweredTurn"`
Expected: PASS (1 passed). If it fails, investigate before continuing (real orchestration bug or harness issue).

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs
git commit -m "test(e2e): SessionRunner audio happy-path scenario"
```

---

### Task 4: Scenario 2 — same-speaker merge within the window

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs`

- [ ] **Step 1: Add the merge test**

Two Other fragments emitted back-to-back (`GapMsBefore = 0`) arrive within the merge window and collapse into one transcript item → one detection → exactly one turn. The discriminating signal is the turn *count* (without merging, two scripted `NewQuestion` results would yield two turns); the merged question text is not asserted (it comes from the scripted classifier).

```csharp
    [Fact]
    public async Task TwoOtherFragments_WithinMergeWindow_ProduceOneTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[]
        {
            new ScriptedUtterance(Speaker.Other, "What is dependency", 0),
            new ScriptedUtterance(Speaker.Other, "injection?", 0), // same speaker, no gap -> merged
        };
        // One detection is expected after the merge. Enqueue a single scripted result; if the merge
        // regressed into two items, the second item would consume no scripted result and the turn
        // count assertion below would fail.
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, new AudioDeviceSelection("mic", "loopback"),
            WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await WaitForStateAsync(session.Id,
            s => s.ConversationTurns.Count >= 1 && s.ConversationTurns.All(t => t.AnswerVersions.Count >= 1),
            StateTimeout);
        await runner.StopAsync(); // full drain — authoritative for the count assertion

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        _host.Sink.Errors.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~TwoOtherFragments_WithinMergeWindow_ProduceOneTurn"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs
git commit -m "test(e2e): SessionRunner same-speaker merge scenario"
```

---

### Task 5: Scenario 3 — speaker-change flush (Other then Me)

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs`

- [ ] **Step 1: Add the speaker-change test**

An Other utterance followed by a Me utterance: the speaker change flushes the buffered Other segment, and the Me utterance follows the deterministic clarification path (no new turn). Exercises frame fan-out across both channels and the speaker-change flush branch.

```csharp
    [Fact]
    public async Task OtherThenMe_FlushesOnSpeakerChange_AndMeCreatesNoTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[]
        {
            new ScriptedUtterance(Speaker.Other, "What is CQRS?", 0),
            new ScriptedUtterance(Speaker.Me, "do you mean the read side?", MergeWindowMs * 2),
        };
        _host.Classifier.Enqueue(NewQuestion("What is CQRS?"));

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, new AudioDeviceSelection("mic", "loopback"),
            WhisperModelSize.Base, "en", AudioSourceMode.Both);

        await WaitForStateAsync(session.Id,
            s => s.ConversationTurns.Count >= 1 && s.ConversationTurns.All(t => t.AnswerVersions.Count >= 1),
            StateTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle(); // the Other turn; Me never creates a turn
        _host.Sink.Errors.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~OtherThenMe_FlushesOnSpeakerChange"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs
git commit -m "test(e2e): SessionRunner speaker-change flush scenario"
```

---

### Task 6: Scenario 4 — audio-source gating drops the disabled channel

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs`

- [ ] **Step 1: Add the gating test**

In `MicrophoneOnly` mode, `runLoopback` is false, so an `Other` (loopback) utterance is dropped at fan-out and never transcribed → zero turns. This is the clean observable: the same Other utterance yields one turn in `Both` mode (Scenario 1) but none here.

```csharp
    [Fact]
    public async Task MicrophoneOnly_DropsOtherLoopbackUtterance_ProducesNoTurn()
    {
        var session = await PersistNewSessionAsync();
        var script = new[] { new ScriptedUtterance(Speaker.Other, "What is DI?", 0) };
        // No classifier result enqueued: the Other frame is gated out and never reaches the pipeline.

        var runner = NewRunner(script);
        await runner.StartAsync(session.Id, new AudioDeviceSelection("mic", "loopback"),
            WhisperModelSize.Base, "en", AudioSourceMode.MicrophoneOnly);

        // Nothing should be produced; let the finite capture drain, then stop and assert.
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().BeEmpty();
        _host.Sink.Errors.Should().BeEmpty();
    }
```

> Note: this test relies on `StopAsync` joining the (finite, fast) loop to completion before asserting. Because the only utterance is gated out at capture, there is no answer to wait for, so it stops immediately after the loop drains.

- [ ] **Step 2: Run the test**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~MicrophoneOnly_DropsOtherLoopbackUtterance"`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/SessionRunnerAudioE2ETests.cs
git commit -m "test(e2e): SessionRunner audio-source gating scenario"
```

---

### Task 7: Full suite + finalize

- [ ] **Step 1: Build the solution clean**

Run: `dotnet build`
Expected: `0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the integration suite**

Run: `dotnet test tests/AIHelperNET.Integration.Tests`
Expected: all four new tests pass. The pre-existing `Scenario1_TwoOtherQuestions` SQLite-lock flakiness may appear under full-suite load — it is unrelated/out of scope (re-run in isolation to confirm green).

- [ ] **Step 3: Confirm the four new tests pass in isolation**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~SessionRunnerAudioE2ETests"`
Expected: 4 passed, 0 failed.

---

## Self-review notes

- **Spec coverage:** Scenario 1 (happy path) → Task 3; Scenario 2 (merge) → Task 4; Scenario 3 (speaker-change flush) → Task 5; the spec's Scenario 4 (audio-source gating) is realized as the cleaner observable "MicrophoneOnly drops Other → 0 turns" in Task 6; production change (injectable window) → Task 1; fakes → Task 2.
- **Type consistency:** `ScriptedUtterance(Speaker, string, int)`, `AudioFrame(float[], Speaker, DateTimeOffset)`, `TranscriptSegment(string, Speaker, DateTimeOffset, float)`, `SessionRunner(.., int segmentMergeWindowMs = 300)`, `WhisperModelSize.Base`, `AudioSourceMode.{Both,MicrophoneOnly}` — all verified against source.
- **No placeholders:** every step has concrete code/commands.
