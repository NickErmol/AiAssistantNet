# Transcription — Silero VAD + Whisper Tuning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the energy-based `VoiceActivityDetector` with Silero VAD (ONNX Runtime), add per-window Whisper processor rebuild with rolling prompt and `WithNoContext()`, and upgrade the default Whisper model to Medium.

**Architecture:** `SileroVadDetector` replaces `VoiceActivityDetector` with the identical `AccumulateSpeechWindows` static signature. A new `VadWindowAccumulator` holds the pure hysteresis state machine (testable without ONNX). `WhisperTranscriptionService` gains `SileroModelProvider` as a second constructor parameter and rebuilds its `WhisperProcessor` per speech window.

**Tech Stack:** .NET 10, Microsoft.ML.OnnxRuntime (CPU), Whisper.NET 1.9.1, xunit 2.9.3, Xunit.SkippableFact

---

## File Map

| File | Action |
|------|--------|
| `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj` | Add `Microsoft.ML.OnnxRuntime` |
| `tests/AIHelperNET.Infrastructure.Tests/AIHelperNET.Infrastructure.Tests.csproj` | Add `Xunit.SkippableFact` |
| `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs` | **New** — pure hysteresis state machine |
| `src/AIHelperNET.Infrastructure/Audio/SileroVadDetector.cs` | **New** — ONNX inference + window assembly |
| `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs` | **Delete** |
| `src/AIHelperNET.Infrastructure/Transcription/SileroModelProvider.cs` | **New** — download + cache silero_vad.onnx |
| `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Per-window processor, rolling prompt, `WithNoContext()` |
| `src/AIHelperNET.Infrastructure/DependencyInjection.cs` | Register `SileroModelProvider` |
| `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs` | Default model Base → Medium |
| `src/AIHelperNET.App/App.xaml.cs` | Pre-warm Silero in background |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` | Default `_whisperModel` Base → Medium |
| `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs` | **New** — pure hysteresis unit tests |
| `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadDetectorTests.cs` | **New** — skippable ONNX integration tests |

---

## Task 1: NuGet Dependencies

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
- Modify: `tests/AIHelperNET.Infrastructure.Tests/AIHelperNET.Infrastructure.Tests.csproj`

- [ ] **Step 1: Add OnnxRuntime to Infrastructure project**

In `src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`, add inside the existing `<ItemGroup>` that contains PackageReferences:

```xml
<PackageReference Include="Microsoft.ML.OnnxRuntime" Version="1.20.1" />
```

- [ ] **Step 2: Add SkippableFact to test project**

In `tests/AIHelperNET.Infrastructure.Tests/AIHelperNET.Infrastructure.Tests.csproj`, add inside the PackageReferences ItemGroup:

```xml
<PackageReference Include="Xunit.SkippableFact" Version="1.4.13" />
```

- [ ] **Step 3: Restore and build**

```
dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
dotnet build tests/AIHelperNET.Infrastructure.Tests/AIHelperNET.Infrastructure.Tests.csproj
```

Expected: both projects build with no errors.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj tests/AIHelperNET.Infrastructure.Tests/AIHelperNET.Infrastructure.Tests.csproj
git commit -m "chore: add OnnxRuntime and SkippableFact NuGet packages"
```

---

## Task 2: VadWindowAccumulator — TDD

**Files:**
- Create: `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs`
- Create: `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs`:

```csharp
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Audio;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Audio;

public sealed class SileroVadHysteresisTests
{
    // Dummy 512-sample chunk — content irrelevant for hysteresis tests.
    private static float[] Chunk => new float[512];

    [Fact]
    public void AllSilence_EmitsNoWindows()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        for (int i = 0; i < 50; i++)
            Collect(acc, windows, 0.1f);

        var final = acc.Flush();
        if (final is not null) windows.Add(final);

        Assert.Empty(windows);
    }

    [Fact]
    public void ShortBurst_BelowMinChunks_IsDiscarded()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm chunks (triggers speech start, chunkCount=2)
        // + 4 speech chunks (chunkCount=6, below MinChunks=8)
        // + 12 silence chunks (SilenceFlushCount reached, discard)
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);  // confirm
        for (int i = 0; i < 4; i++) Collect(acc, windows, 0.9f);  // speech
        for (int i = 0; i < 12; i++) Collect(acc, windows, 0.1f); // silence

        Assert.Empty(windows);
    }

    [Fact]
    public void NormalSpeech_EmitsOneWindow()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm + 20 speech + 12 silence → one window
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < 20; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < 12; i++) Collect(acc, windows, 0.1f);

        Assert.Single(windows);
        Assert.Equal(Speaker.Other, windows[0].Speaker);
    }

    [Fact]
    public void MaxWindowReached_ForcesFlush()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // 2 confirm + (MaxChunks - 2) speech = exactly MaxChunks total → force-flush
        for (int i = 0; i < 2; i++) Collect(acc, windows, 0.9f);
        for (int i = 0; i < VadWindowAccumulator.MaxChunks - 2; i++) Collect(acc, windows, 0.9f);

        Assert.Single(windows);
    }

    [Fact]
    public void NoisyOnset_AlternatingProbabilities_NeverStartsSpeech()
    {
        var acc = new VadWindowAccumulator();
        var windows = new List<SpeechWindow>();

        // alternating 0.4/0.6 — never 2 consecutive chunks ≥ 0.5
        for (int i = 0; i < 20; i++)
            Collect(acc, windows, i % 2 == 0 ? 0.4f : 0.6f);

        var final = acc.Flush();
        if (final is not null) windows.Add(final);

        Assert.Empty(windows);
    }

    private static void Collect(VadWindowAccumulator acc, List<SpeechWindow> windows, float prob)
    {
        var w = acc.Feed(prob, Chunk, Speaker.Other);
        if (w is not null) windows.Add(w);
    }
}
```

- [ ] **Step 2: Run to confirm they fail**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SileroVadHysteresisTests"
```

Expected: FAIL — `The type or namespace 'VadWindowAccumulator' could not be found`

- [ ] **Step 3: Implement VadWindowAccumulator**

Create `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs`:

```csharp
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Infrastructure.Audio;

/// <summary>
/// Pure hysteresis state machine for Silero VAD probabilities.
/// Caller feeds per-chunk probabilities; this class manages the speech/silence window lifecycle.
/// </summary>
public sealed class VadWindowAccumulator
{
    private const float SpeechStartThreshold    = 0.50f;
    private const float SpeechContinueThreshold = 0.35f;
    private const int   StartConfirmCount = 2;   // consecutive chunks ≥ 0.5 required to start
    private const int   SilenceFlushCount = 12;  // sub-threshold chunks before flush (~375 ms)
    public  const int   MinChunks = 8;           // minimum chunks to emit a window (~250 ms)
    public  const int   MaxChunks = 240;         // force-flush threshold (~7.5 s)

    private bool          _inSpeech;
    private int           _confirmCount;
    private int           _silenceCount;
    private int           _chunkCount;
    private readonly List<float> _confirmBuffer = new();
    private readonly List<float> _buffer        = new();
    private Speaker       _lastSpeaker;

    /// <summary>
    /// Feed one 512-sample chunk with its Silero speech probability.
    /// Returns a completed <see cref="SpeechWindow"/> when the window is ready; otherwise null.
    /// </summary>
    public SpeechWindow? Feed(float probability, float[] samples, Speaker speaker)
    {
        if (!_inSpeech)
        {
            if (probability >= SpeechStartThreshold)
            {
                _confirmCount++;
                _confirmBuffer.AddRange(samples);
                _lastSpeaker = speaker;

                if (_confirmCount >= StartConfirmCount)
                {
                    _inSpeech     = true;
                    _silenceCount = 0;
                    _chunkCount   = _confirmCount;
                    _buffer.AddRange(_confirmBuffer);
                    _confirmBuffer.Clear();
                    _confirmCount = 0;

                    if (_chunkCount >= MaxChunks) return FlushWindow();
                }
            }
            else
            {
                _confirmCount = 0;
                _confirmBuffer.Clear();
            }
            return null;
        }

        // In speech
        _buffer.AddRange(samples);
        _lastSpeaker  = speaker;
        _chunkCount++;
        _silenceCount = probability >= SpeechContinueThreshold ? 0 : _silenceCount + 1;

        if (_chunkCount >= MaxChunks)
            return FlushWindow();

        if (_silenceCount >= SilenceFlushCount)
        {
            if (_chunkCount >= MinChunks) return FlushWindow();
            Reset();
        }

        return null;
    }

    /// <summary>Force-flushes remaining buffer (call on stream end). Returns null if buffer is too short.</summary>
    public SpeechWindow? Flush()
    {
        if (!_inSpeech || _chunkCount < MinChunks) { Reset(); return null; }
        return FlushWindow();
    }

    private SpeechWindow FlushWindow()
    {
        var win = new SpeechWindow([.. _buffer], _lastSpeaker);
        Reset();
        return win;
    }

    private void Reset()
    {
        _inSpeech     = false;
        _confirmCount = 0;
        _silenceCount = 0;
        _chunkCount   = 0;
        _buffer.Clear();
        _confirmBuffer.Clear();
    }
}
```

- [ ] **Step 4: Run tests — should all pass**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SileroVadHysteresisTests"
```

Expected: 5 tests PASS.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs
git commit -m "feat: add VadWindowAccumulator with hysteresis tests"
```

---

## Task 3: SileroModelProvider

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/SileroModelProvider.cs`

- [ ] **Step 1: Create SileroModelProvider**

Create `src/AIHelperNET.Infrastructure/Transcription/SileroModelProvider.cs`:

```csharp
using System.IO;
using System.Net.Http;
using AIHelperNET.Infrastructure.Common;
using Microsoft.ML.OnnxRuntime;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class SileroModelProvider : IAsyncDisposable
{
    // Silero VAD v4 ONNX model from official GitHub release.
    private const string ModelUrl = "https://github.com/snakers4/silero-vad/raw/v4.0/files/silero_vad.onnx";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private InferenceSession? _session;

    public SileroModelProvider(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public async Task<InferenceSession> GetSessionAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_session is not null) return _session;
            var path = Path.Combine(AppPaths.ModelsDir, "silero_vad.onnx");
            if (!File.Exists(path)) await DownloadAsync(path, ct);
            _session = new InferenceSession(path);
            return _session;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DownloadAsync(string targetPath, CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();
        using var httpClient = _httpClientFactory.CreateClient(nameof(SileroModelProvider));
        using var response   = await httpClient.GetAsync(ModelUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var file   = File.Create(targetPath);
        await stream.CopyToAsync(file, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _session?.Dispose();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
```

Expected: build succeeds, no errors.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Infrastructure/Transcription/SileroModelProvider.cs
git commit -m "feat: add SileroModelProvider for ONNX model download and cache"
```

---

## Task 4: SileroVadDetector

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Audio/SileroVadDetector.cs`

- [ ] **Step 1: Create SileroVadDetector**

Create `src/AIHelperNET.Infrastructure/Audio/SileroVadDetector.cs`:

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Transcription;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

public static class SileroVadDetector
{
    private const int ChunkSize = 512; // 32 ms at 16 kHz

    public static async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        SileroModelProvider modelProvider,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var session  = await modelProvider.GetSessionAsync(ct);
        var acc      = new VadWindowAccumulator();
        var leftover = new List<float>();

        // LSTM state tensors — reset after each emitted window.
        var h = new float[2 * 1 * 64]; // [2,1,64]
        var c = new float[2 * 1 * 64]; // [2,1,64]

        int totalChunks = 0, speechChunks = 0;

        await foreach (var frame in frames.WithCancellation(ct))
        {
            leftover.AddRange(frame.Samples);

            while (leftover.Count >= ChunkSize)
            {
                var chunk = leftover.Take(ChunkSize).ToArray();
                leftover.RemoveRange(0, ChunkSize);
                totalChunks++;

                var prob = RunInference(session, chunk, h, c);
                if (prob >= 0.35f) speechChunks++;

                var window = acc.Feed(prob, chunk, frame.Speaker);
                if (window is not null)
                {
                    // Reset LSTM so the next window starts with a clean state.
                    Array.Clear(h);
                    Array.Clear(c);
                    Log.Information("SileroVAD: emitting SpeechWindow speaker={S} samples={N}",
                        window.Speaker, window.Samples.Length);
                    yield return window;
                }
            }
        }

        // Flush any remaining buffered speech.
        var final = acc.Flush();
        if (final is not null)
        {
            Log.Information("SileroVAD: flushing final SpeechWindow speaker={S} samples={N}",
                final.Speaker, final.Samples.Length);
            yield return final;
        }

        Log.Information("SileroVAD: done — totalChunks={T} speechChunks={S}", totalChunks, speechChunks);
    }

    private static float RunInference(InferenceSession session, float[] chunk, float[] h, float[] c)
    {
        var inputTensor = new DenseTensor<float>(chunk,        new[] { 1, ChunkSize });
        var hTensor     = new DenseTensor<float>(h,            new[] { 2, 1, 64 });
        var cTensor     = new DenseTensor<float>(c,            new[] { 2, 1, 64 });
        var srTensor    = new DenseTensor<long>(new[] { 16000L }, new[] { 1 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("sr",    srTensor),
            NamedOnnxValue.CreateFromTensor("h",     hTensor),
            NamedOnnxValue.CreateFromTensor("c",     cTensor),
        };

        using var results = session.Run(inputs);

        float prob = results.First(r => r.Name == "output").AsTensor<float>()[0, 0];
        results.First(r => r.Name == "hn").AsTensor<float>().ToArray().CopyTo(h, 0);
        results.First(r => r.Name == "cn").AsTensor<float>().ToArray().CopyTo(c, 0);
        return prob;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
```

Expected: build succeeds, no errors.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Infrastructure/Audio/SileroVadDetector.cs
git commit -m "feat: add SileroVadDetector using ONNX Runtime and VadWindowAccumulator"
```

---

## Task 5: SileroVadDetectorTests (Skippable)

**Files:**
- Create: `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadDetectorTests.cs`

These tests require `silero_vad.onnx` on disk. They skip automatically in CI and run on the dev machine when the model is present.

- [ ] **Step 1: Create skippable tests**

Create `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadDetectorTests.cs`:

```csharp
using System.Net.Http;
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Audio;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Transcription;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Audio;

public sealed class SileroVadDetectorTests
{
    private static string ModelPath => System.IO.Path.Combine(AppPaths.ModelsDir, "silero_vad.onnx");

    private static SileroModelProvider MakeProvider()
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient());
        return new SileroModelProvider(factory);
    }

    [SkippableFact]
    public async Task GetSessionAsync_ReturnsSession_WhenModelExists()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();
        var session  = await provider.GetSessionAsync(CancellationToken.None);
        Assert.NotNull(session);
        await provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task SilenceAudio_ProducesNoWindows()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();

        // 3 seconds of silence at 16 kHz
        var silence = new float[16000 * 3];
        var frames  = new[] { new AudioFrame(silence, Speaker.Other, DateTimeOffset.UtcNow) };

        var windows = new List<SpeechWindow>();
        await foreach (var w in SileroVadDetector.AccumulateSpeechWindows(
            frames.AsAsync(), provider, CancellationToken.None))
        {
            windows.Add(w);
        }

        Assert.Empty(windows);
        await provider.DisposeAsync();
    }

    [SkippableFact]
    public async Task GetSessionAsync_ReturnsSameInstance_OnSecondCall()
    {
        Skip.If(!System.IO.File.Exists(ModelPath), "silero_vad.onnx not found — skipping ONNX test");

        var provider = MakeProvider();
        var s1 = await provider.GetSessionAsync(CancellationToken.None);
        var s2 = await provider.GetSessionAsync(CancellationToken.None);
        Assert.Same(s1, s2);
        await provider.DisposeAsync();
    }
}

file static class AsyncExtensions
{
    public static async IAsyncEnumerable<T> AsAsync<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
```

- [ ] **Step 2: Build and run — tests should skip**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SileroVadDetectorTests"
```

Expected: 3 tests SKIP (unless `silero_vad.onnx` is present in `AppPaths.ModelsDir`, in which case they should PASS).

- [ ] **Step 3: Commit**

```
git add tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadDetectorTests.cs
git commit -m "test: add skippable SileroVadDetector integration tests"
```

---

## Task 6: Update WhisperTranscriptionService

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`

- [ ] **Step 1: Replace the file contents**

Replace `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` with:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Infrastructure.Audio;
using Whisper.net;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(
    WhisperModelProvider whisperModels,
    SileroModelProvider  sileroModels) : ITranscriptionService
{
    // Serialises Build() across mic and loopback tasks. Concurrent KV-cache allocation for
    // medium/large models causes both builds to stall indefinitely; sequential builds complete.
    private static readonly SemaphoreSlim _buildLock = new(1, 1);

    private const int MinWords = 3;

    private const string InitialPrompt =
        "Technical interview. Software engineering, system design, algorithms, data structures, coding.";

    private static readonly HashSet<string> HallucinationPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you", "thanks for watching", "thanks for listening",
        "please subscribe", "like and subscribe", "see you next time",
        "subtitles by", "transcribed by",
    };

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var factory = await whisperModels.GetFactoryAsync(model, ct);
        var lang    = string.IsNullOrWhiteSpace(language) || language == "auto" ? null : language;

        string? lastEmitted = null;

        await foreach (var window in SileroVadDetector.AccumulateSpeechWindows(frames, sileroModels, ct))
        {
            await _buildLock.WaitAsync(ct);
            WhisperProcessor processor;
            try
            {
                processor = factory.CreateBuilder()
                    .WithLanguage(lang ?? "en")
                    .WithTemperature(0)            // greedy decoding — no random word substitutions
                    .WithNoContext()               // prevent stale KV-cache from previous windows
                    .WithPrompt(lastEmitted ?? InitialPrompt) // rolling context for vocabulary continuity
                    .WithNoSpeechThreshold(0.6f)
                    .WithSingleSegment()
                    .Build();
            }
            finally { _buildLock.Release(); }

            await using var _ = (IAsyncDisposable)processor;

            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (IsKnownHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                lastEmitted = seg.Text.Trim();
                yield return new TranscriptSegment(
                    lastEmitted,
                    window.Speaker,
                    DateTimeOffset.UtcNow,
                    seg.Probability);
            }
        }
    }

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsKnownHallucination(string text)
    {
        var trimmed = text.Trim('.', '!', '?', ' ');
        return HallucinationPhrases.Contains(trimmed);
    }

    private static bool IsNearDuplicate(string current, string? previous)
    {
        if (previous is null) return false;
        var a = Tokenize(current);
        var b = Tokenize(previous);
        return QuestionDetector.Jaccard(a, b) >= 0.85;
    }

    private static HashSet<string> Tokenize(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries)];
}
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
```

Expected: build succeeds. If `WithNoContext()` does not exist in Whisper.net 1.9.1, remove that single line — the per-window processor rebuild already prevents KV-cache contamination.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
git commit -m "feat: rebuild WhisperProcessor per window with rolling prompt and WithNoContext"
```

---

## Task 7: DI Registration + App Pre-warm

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs`

- [ ] **Step 1: Register SileroModelProvider in DI**

In `src/AIHelperNET.Infrastructure/DependencyInjection.cs`, in the `// Audio & transcription` block, add two lines after the existing `WhisperModelProvider` registration:

```csharp
// Audio & transcription
services.AddHttpClient(nameof(WhisperModelProvider));
services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
services.AddSingleton<IAudioLevelMonitor, AudioLevelMonitor>();
services.AddSingleton<WhisperModelProvider>();
services.AddHttpClient(nameof(SileroModelProvider));   // ← add this
services.AddSingleton<SileroModelProvider>();           // ← add this
services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
```

Also add the using for the Transcription namespace — it's already present (`using AIHelperNET.Infrastructure.Transcription;`) so no change needed there.

- [ ] **Step 2: Add Silero pre-warm in App.OnStartup**

In `src/AIHelperNET.App/App.xaml.cs`, add a `using AIHelperNET.Infrastructure.Transcription;` if not already present (it already is), then add a call to `PreWarmSileroModel()` right after `PreWarmWhisperModel()` in `OnStartup`:

```csharp
        ScreenGrabber.StartTracking();
        overlay.Show();
        WireHotkeys(overlay);
        PreWarmWhisperModel();
        PreWarmSileroModel();   // ← add this line
    }
```

Then add the method after `PreWarmWhisperModel()`:

```csharp
    private void PreWarmSileroModel()
    {
        var provider = _host.Services.GetRequiredService<SileroModelProvider>();
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Silero: pre-warming VAD model in background…");
                await provider.GetSessionAsync(CancellationToken.None);
                Log.Information("Silero: VAD model ready");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Silero: pre-warm failed");
            }
        });
    }
```

- [ ] **Step 3: Build full solution**

```
dotnet build
```

Expected: full solution builds with no errors.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs src/AIHelperNET.App/App.xaml.cs
git commit -m "feat: register SileroModelProvider in DI and pre-warm at startup"
```

---

## Task 8: Default Model → Medium

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Update JsonSettingsStore default**

In `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`, change `DefaultSettings()`:

```csharp
// Before:
private static AppSettingsDto DefaultSettings() => new(
    AiBackend.Claude,
    WhisperModelSize.Base,   // ← change this
    ...

// After:
private static AppSettingsDto DefaultSettings() => new(
    AiBackend.Claude,
    WhisperModelSize.Medium, // ← updated
    Domain.ValueObjects.AnswerSettings.Default,
    Domain.ValueObjects.CodeProfile.Empty,
    null,
    null);
```

- [ ] **Step 2: Update SettingsViewModel default**

In `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`, line 25:

```csharp
// Before:
[ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.Base;

// After:
[ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.Medium;
```

- [ ] **Step 3: Build**

```
dotnet build
```

Expected: build succeeds.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
git commit -m "feat: upgrade default Whisper model from Base to Medium"
```

---

## Task 9: Delete VoiceActivityDetector, Run All Tests, Final Commit

**Files:**
- Delete: `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs`

- [ ] **Step 1: Delete VoiceActivityDetector.cs**

```
git rm src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs
```

- [ ] **Step 2: Build to confirm no remaining references**

```
dotnet build
```

Expected: build succeeds. If there are any remaining references to `VoiceActivityDetector` (other than in comments), fix them.

- [ ] **Step 3: Run all tests**

```
dotnet test
```

Expected: all tests pass (skippable ONNX tests skip unless model file is present). No regressions in `SessionRunnerTests`, `SileroVadHysteresisTests`, or any other test project.

- [ ] **Step 4: Commit**

```
git add -A
git commit -m "feat: delete VoiceActivityDetector — replaced by SileroVadDetector"
```

---

## Manual E2E Checklist (run before opening PR)

After all tasks pass:

- [ ] Start session in AudioAndScreen mode
- [ ] Speak into microphone — verify mic transcript appears (Speaker.Me)
- [ ] Play audio through speakers — verify loopback transcript appears (Speaker.Other)
- [ ] Sustain speech for 10+ seconds — verify no repetition hallucinations
- [ ] Open Settings → Audio → verify Model combobox shows Medium as selected
- [ ] Change model in settings, save, reopen — verify selection persists
- [ ] Toggle session off and on — verify pipeline restarts cleanly
