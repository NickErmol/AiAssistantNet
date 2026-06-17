# Transcription Latency Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reduce perceived transcription latency and diagnose the large-turbo 10–15s delay by instrumenting build/inference timing, tuning VAD windowing to chop long speech into smaller final windows, and — only if measurement justifies it — reusing the WhisperProcessor instead of rebuilding it per window.

**Architecture:** Three components in dependency order. Component 1 (timing logs) is purely additive and ships first; its numbers gate Component 3. Component 2 changes two VAD constants. Component 3 is a conditional structural change to `WhisperTranscriptionService`.

**Tech Stack:** .NET 10, C#, Whisper.net 1.9.1 (Vulkan/CPU), Serilog, xUnit. Spec: `docs/superpowers/specs/2026-06-17-transcription-latency-design.md`.

**Convention:** All commits in this plan end with the repo's trailer (omitted from the commands below for brevity):
```
Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
Work happens on branch `feature/transcription-latency` (already created off `develop`). Stop any running overlay app before building (it locks output DLLs).

---

## File Structure

- **Create:** `src/AIHelperNET.Infrastructure/Transcription/TranscriptionMetrics.cs` — pure timing math (RTF, audio-seconds). One responsibility: turn raw numbers into diagnostic values. No I/O.
- **Create:** `tests/AIHelperNET.Infrastructure.Tests/Transcription/TranscriptionMetricsTests.cs` — unit tests for the helper.
- **Modify:** `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` — wire timing (Component 1) and conditional processor reuse (Component 3).
- **Modify:** `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs` — VAD constants (Component 2).
- **Modify:** `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs` — long-speech chopping test (Component 2).

---

## Component 1 — Timing instrumentation

### Task 1: Pure timing helper (`TranscriptionMetrics`)

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/TranscriptionMetrics.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/TranscriptionMetricsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `tests/AIHelperNET.Infrastructure.Tests/Transcription/TranscriptionMetricsTests.cs`:

```csharp
using AIHelperNET.Infrastructure.Transcription;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public sealed class TranscriptionMetricsTests
{
    [Fact]
    public void WindowAudioSeconds_16kHzMono_ConvertsSampleCount()
    {
        // 16000 samples at 16 kHz == 1.0 second
        Assert.Equal(1.0f, TranscriptionMetrics.WindowAudioSeconds(16000));
        Assert.Equal(0.5f, TranscriptionMetrics.WindowAudioSeconds(8000));
    }

    [Fact]
    public void RealtimeFactor_SlowerThanRealtime_IsGreaterThanOne()
    {
        // 2000 ms to transcribe 1.0 s of audio -> RTF 2.0
        Assert.Equal(2.0, TranscriptionMetrics.RealtimeFactor(2000, 1.0f), precision: 3);
    }

    [Fact]
    public void RealtimeFactor_FasterThanRealtime_IsLessThanOne()
    {
        Assert.Equal(0.5, TranscriptionMetrics.RealtimeFactor(500, 1.0f), precision: 3);
    }

    [Fact]
    public void RealtimeFactor_ZeroAudio_ReturnsZeroInsteadOfDivideByZero()
    {
        Assert.Equal(0.0, TranscriptionMetrics.RealtimeFactor(100, 0f));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~TranscriptionMetricsTests"`
Expected: FAIL to compile — `TranscriptionMetrics` does not exist.

- [ ] **Step 3: Write minimal implementation**

Create `src/AIHelperNET.Infrastructure/Transcription/TranscriptionMetrics.cs`:

```csharp
namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>
/// Pure helpers for transcription timing diagnostics. No I/O — safe to unit test.
/// </summary>
public static class TranscriptionMetrics
{
    private const int SampleRate = 16000;

    /// <summary>Audio duration in seconds for a 16 kHz mono sample buffer.</summary>
    public static float WindowAudioSeconds(int sampleCount) => sampleCount / (float)SampleRate;

    /// <summary>
    /// Real-time factor: inference wall-time relative to the audio duration.
    /// A value &gt; 1 means transcription is slower than real time (the root of the
    /// "transcript appears N seconds late" symptom). Returns 0 for non-positive audio
    /// to avoid divide-by-zero.
    /// </summary>
    public static double RealtimeFactor(long inferMs, float windowAudioSec) =>
        windowAudioSec <= 0f ? 0d : inferMs / (windowAudioSec * 1000d);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~TranscriptionMetricsTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Transcription/TranscriptionMetrics.cs tests/AIHelperNET.Infrastructure.Tests/Transcription/TranscriptionMetricsTests.cs
git commit -m "feat(transcription): pure timing metrics helper (RTF, audio-seconds)"
```

### Task 2: Wire timing logs into the transcription loop

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`

This task buffers `ProcessAsync` output before yielding so the inference stopwatch measures Whisper only (not downstream UI marshalling), and logs one metadata-only line per window. No unit test (it is logging over real Whisper); verified manually in Task 3.

- [ ] **Step 1: Add the Serilog using**

At the top of `WhisperTranscriptionService.cs`, add to the using block:

```csharp
using Serilog;
```

- [ ] **Step 2: Time the build**

In `TranscribeAsync`, wrap the existing build block. Replace:

```csharp
            await _buildLock.WaitAsync(ct);
            WhisperProcessor processor;
            try
            {
                processor = factory.CreateBuilder()
                    .WithLanguage(lang ?? "en")
                    .WithTemperature(0)            // greedy decoding — no random word substitutions
                    .WithNoContext()               // prevent stale KV-cache from previous windows
                    .WithPrompt(BuildPrompt())     // rolling context + glossary bias for every window
                    .WithNoSpeechThreshold(0.6f)
                    .WithSingleSegment()
                    .Build();
            }
            finally { _buildLock.Release(); }
```

with:

```csharp
            await _buildLock.WaitAsync(ct);
            WhisperProcessor processor;
            var buildSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                processor = factory.CreateBuilder()
                    .WithLanguage(lang ?? "en")
                    .WithTemperature(0)            // greedy decoding — no random word substitutions
                    .WithNoContext()               // prevent stale KV-cache from previous windows
                    .WithPrompt(BuildPrompt())     // rolling context + glossary bias for every window
                    .WithNoSpeechThreshold(0.6f)
                    .WithSingleSegment()
                    .Build();
            }
            finally { _buildLock.Release(); }
            buildSw.Stop();
```

- [ ] **Step 3: Buffer inference, time it, then log**

Replace the existing inference loop:

```csharp
            await using var _ = (IAsyncDisposable)processor;

            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (IsKnownHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                var text = seg.Text.Trim();
                lastEmitted = text;
                recent.Enqueue(text);
                while (recent.Count > RecentContextSegments) recent.Dequeue();
                yield return new TranscriptSegment(text, window.Speaker, DateTimeOffset.UtcNow, seg.Probability);
            }
```

with (buffer first so the stopwatch excludes downstream consumer time, then log, then filter/yield):

```csharp
            await using var _ = (IAsyncDisposable)processor;

            var produced = new List<SegmentData>();
            var inferSw = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
                produced.Add(seg);
            inferSw.Stop();

            var audioSec = TranscriptionMetrics.WindowAudioSeconds(window.Samples.Length);
            Log.Information(
                "WhisperTiming model={Model} speaker={Speaker} windowAudioSec={AudioSec:F2} " +
                "buildMs={BuildMs} inferMs={InferMs} rtf={Rtf:F2}",
                model, window.Speaker, audioSec,
                buildSw.ElapsedMilliseconds, inferSw.ElapsedMilliseconds,
                TranscriptionMetrics.RealtimeFactor(inferSw.ElapsedMilliseconds, audioSec));

            foreach (var seg in produced)
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (IsKnownHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                var text = seg.Text.Trim();
                lastEmitted = text;
                recent.Enqueue(text);
                while (recent.Count > RecentContextSegments) recent.Dequeue();
                yield return new TranscriptSegment(text, window.Speaker, DateTimeOffset.UtcNow, seg.Probability);
            }
```

Note: `SegmentData` is Whisper.net's segment type (already the element type of `processor.ProcessAsync`), so no new using is needed.

- [ ] **Step 4: Build the solution to verify it compiles**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 5: Run the Infrastructure test suite (no regressions)**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests`
Expected: PASS (all existing + the 4 new metric tests).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
git commit -m "feat(transcription): log per-window build/inference timing + RTF (metadata only)"
```

### Task 3: Manual measurement run (gates Component 3)

**Files:** none (run + observe).

- [ ] **Step 1: Launch the app on the large model**

Use the `run-aihelper` skill (or run the Debug exe). In Settings, set the Whisper model to **Large Turbo** and start a session.

- [ ] **Step 2: Drive a few questions via TTS → loopback**

Use the established `Speaker.Other` technique: play 3–4 spoken questions through the default render device so loopback captures them. Speak/play at least one long (10s+) continuous sentence.

- [ ] **Step 3: Read the timing lines from the log**

Open the newest `D:\AIHelperNET\logs\log-2026*.txt` and find the `WhisperTiming` lines. Record, per window: `buildMs`, `inferMs`, `rtf`.

- [ ] **Step 4: Decide the Component 3 gate**

- If **`buildMs` is a significant share** of total per-window time (e.g. build is the same order as inference, or hundreds of ms): **proceed to Component 3.**
- If **`buildMs` is negligible** vs `inferMs` (inference dominates): **skip Component 3** — the only levers are model size + smaller windows (Component 2). Record this conclusion in the spec file under a new "Measurement results" heading and commit.

---

## Component 2 — VAD windowing tuning

### Task 4: Chop long speech into smaller final windows

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs`
- Modify: `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs`

- [ ] **Step 1: Write the failing test for long-speech chopping**

In `SileroVadHysteresisTests.cs`, add:

```csharp
    [Fact]
    public void LongContinuousSpeech_ChopsIntoMultipleWindows()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // Feed well over two MaxChunks worth of continuous speech with no pause.
        // Force-flush at MaxChunks means a long monologue yields multiple finals
        // instead of one giant block.
        for (int i = 0; i < VadWindowAccumulator.MaxChunks * 2 + 10; i++)
            Collect(acc, windows, 0.9f);

        Assert.True(windows.Count >= 2,
            $"expected >= 2 chopped windows, got {windows.Count}");
    }
```

- [ ] **Step 2: Run it against current constants to confirm the test is meaningful**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~LongContinuousSpeech_ChopsIntoMultipleWindows"`
Expected: PASS already (the test asserts behavior that holds at any `MaxChunks`; it guards against regressions when we lower the constant). If it does not pass, stop and investigate before changing constants.

- [ ] **Step 3: Lower the windowing constants**

In `VadWindowAccumulator.cs`, change:

```csharp
    private const int   SilenceFlushCount = 12;  // sub-threshold chunks before flush (~375 ms)
    public  const int   MinChunks = 8;           // minimum chunks to emit a window (~250 ms)
    public  const int   MaxChunks = 240;         // force-flush threshold (~7.5 s)
```

to:

```csharp
    private const int   SilenceFlushCount = 8;   // sub-threshold chunks before flush (~250 ms)
    public  const int   MinChunks = 8;           // minimum chunks to emit a window (~250 ms)
    public  const int   MaxChunks = 112;         // force-flush threshold (~3.5 s) — chop long speech
```

(32 ms/chunk: 8 → ~256 ms, 112 → ~3.58 s. Starting values; finalized in Task 5.)

- [ ] **Step 4: Run the full VAD test file (no regressions)**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SileroVadHysteresisTests"`
Expected: PASS. The existing tests use `MaxChunks - 2` and silence runs of 12 (≥ new SilenceFlushCount 8), so they remain valid; the new chopping test still passes at `MaxChunks = 112`.

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs
git commit -m "feat(audio): chop long speech into ~3.5s windows + flush ~125ms sooner"
```

### Task 5: Manual A/B tune of the VAD constants

**Files:** `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs` (only if numbers need adjusting).

- [ ] **Step 1: Run a live session and judge responsiveness**

Launch the app, drive questions via TTS → loopback including long sentences. Watch the transcript pane: confirm long speech now surfaces in ~3.5s chunks (no long dead air) and short questions appear shortly after the speaker stops.

- [ ] **Step 2: Check for clipped trailing words**

If the lower `SilenceFlushCount` (8) is cutting the last word off utterances, raise it (e.g. to 10 ≈ 320 ms) and rebuild. If long chunks still feel too long, lower `MaxChunks` further (e.g. 96 ≈ 3.1 s). Re-run Step 1.

- [ ] **Step 3: Commit any adjustment**

```bash
git add src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs
git commit -m "tune(audio): finalize VAD flush/window constants from live A/B"
```

(If no change was needed, skip this commit.)

---

## Component 3 — Per-window rebuild cost (CONDITIONAL on Task 3 Step 4)

> Execute this component **only if Task 3 concluded `buildMs` is significant.** Otherwise skip the entire component.

Because `glossaryDomains` and `language` are constants for the whole `TranscribeAsync` call (read at session start), and dropping the rolling-context prompt removes the only per-window-varying input, the processor can be built **once per stream** and reused for every window. This eliminates the per-window KV-cache reallocation.

### Task 6: Build the processor once and reuse it

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`

- [ ] **Step 1: Build the stable-prompt processor before the window loop**

In `TranscribeAsync`, immediately after `var recent = new Queue<string>(RecentContextSegments);` and the local `BuildPrompt()` function, build the processor once. The stable prompt drops rolling context (`recent` stays empty here) but keeps `InitialPrompt` + the glossary suffix:

```csharp
        string BuildStablePrompt()
        {
            var suffix = glossaryDomains.Count == 0
                ? string.Empty
                : glossary.BuildPromptSuffix(glossaryDomains, string.Empty, GlossaryWordBudget);
            return suffix.Length == 0 ? InitialPrompt : $"{InitialPrompt} {suffix}";
        }

        await _buildLock.WaitAsync(ct);
        WhisperProcessor processor;
        var buildSw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            processor = factory.CreateBuilder()
                .WithLanguage(lang ?? "en")
                .WithTemperature(0)
                .WithNoContext()
                .WithPrompt(BuildStablePrompt())   // built ONCE — no per-window rebuild
                .WithNoSpeechThreshold(0.6f)
                .WithSingleSegment()
                .Build();
        }
        finally { _buildLock.Release(); }
        buildSw.Stop();
        await using var processorScope = (IAsyncDisposable)processor;
```

- [ ] **Step 2: Remove the per-window build and reuse the single processor**

Inside the `await foreach (var window ...)` loop, delete the per-window build block (the `_buildLock`/`buildSw`/`factory.CreateBuilder()…Build()` added in Task 2 and the original) and the per-window `await using var _ = (IAsyncDisposable)processor;`. Keep the buffered inference + timing log, but reference the outer `processor` and report `buildMs=0` for reused windows. The inference section becomes:

```csharp
            var produced = new List<SegmentData>();
            var inferSw = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
                produced.Add(seg);
            inferSw.Stop();

            var audioSec = TranscriptionMetrics.WindowAudioSeconds(window.Samples.Length);
            Log.Information(
                "WhisperTiming model={Model} speaker={Speaker} windowAudioSec={AudioSec:F2} " +
                "buildMs={BuildMs} inferMs={InferMs} rtf={Rtf:F2}",
                model, window.Speaker, audioSec,
                0L, inferSw.ElapsedMilliseconds,
                TranscriptionMetrics.RealtimeFactor(inferSw.ElapsedMilliseconds, audioSec));
```

(The one-time `buildSw.ElapsedMilliseconds` is still visible — log it once before the loop with a distinct message:)

```csharp
        Log.Information("WhisperProcessorBuilt model={Model} buildMs={BuildMs}",
            model, buildSw.ElapsedMilliseconds);
```

Remove the now-unused per-window `BuildPrompt()` local function and the `lastEmitted`/`recent` machinery **only if** they are no longer referenced by the dedup filter. Note: `lastEmitted` is still used by `IsNearDuplicate` and `recent` was only feeding the dropped rolling prompt — so keep `lastEmitted`, and delete `recent`/`RecentContextSegments`/`RecentContextWordCap`/`LastWords` if nothing else references them. Verify with a search before deleting.

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
Expected: Build succeeded, 0 warnings. (If a warning fires for an unused private member, remove that member.)

- [ ] **Step 4: Run the Infrastructure suite**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests`
Expected: PASS.

- [ ] **Step 5: Manual verification — build happens once**

Launch on Large Turbo, drive several questions. In the log confirm: exactly one `WhisperProcessorBuilt` line per session per stream, and every `WhisperTiming` line shows `buildMs=0`. Compare `inferMs`/`rtf` and the felt latency against the Task 3 baseline.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
git commit -m "perf(transcription): build WhisperProcessor once per stream (drop per-window rolling prompt)"
```

---

## Final verification

- [ ] **Run the full solution build and test suite**

Run: `dotnet build` then `dotnet test`
Expected: Build clean; tests green (allow the known pre-existing flakes noted in project memory — Scenario4 baseline + SQLite shared-cache lock).

- [ ] **Update the spec with measurement results**

Add a short "Measurement results" section to `docs/superpowers/specs/2026-06-17-transcription-latency-design.md` recording the before/after RTF and whether Component 3 was executed. Commit.

- [ ] **Open the PR**

```bash
git push -u origin feature/transcription-latency
gh pr create --base develop --title "Transcription latency: instrument + VAD tuning + processor reuse" \
  --body "Per-window build/inference timing logs (RTF, metadata-only); VAD chops long speech into ~3.5s windows and flushes ~125ms sooner; processor built once per stream when build proved expensive. Spec: docs/superpowers/specs/2026-06-17-transcription-latency-design.md"
```
(Follow gitflow: PR targets `develop`, never `master`.)
