# AIHelperNET — Phase 5: Infrastructure – Audio & Transcription

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 4 complete.

**Goal:** Implement WASAPI audio capture (mic + loopback), PCM resampling, voice-activity detection, and Whisper.net-based transcription with near-duplicate filtering.

**Architecture:** `NAudioCaptureService` merges two `WasapiCapture` streams via a `Channel<AudioFrame>`. `WhisperTranscriptionService` gates on VAD windows and deduplicates using `QuestionDetector.Jaccard` from Domain.

**Tech Stack:** NAudio 2.x, Whisper.net 1.x

---

### Task 28: Resampler

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Audio/Resampler.cs`

- [ ] **Step 1: Implement**

Whisper expects 16 kHz mono float32 PCM. NAudio delivers whatever the device format is (often 44100 Hz stereo int16). This utility converts any WaveFormat to 16 kHz mono float.

```csharp
// src/AIHelperNET.Infrastructure/Audio/Resampler.cs
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace AIHelperNET.Infrastructure.Audio;

public static class Resampler
{
    private static readonly WaveFormat Target = WaveFormat.CreateIeeeFloatWaveFormat(16000, 1);

    public static float[] To16kMonoFloat(byte[] buffer, int bytesRecorded, WaveFormat sourceFormat)
    {
        using var raw = new RawSourceWaveStream(buffer, 0, bytesRecorded, sourceFormat);

        ISampleProvider samples = sourceFormat.Encoding == WaveFormatEncoding.IeeeFloat
            ? raw.ToSampleProvider()
            : raw.ToSampleProvider();

        // Mono down-mix if stereo
        ISampleProvider mono = sourceFormat.Channels == 1
            ? samples
            : new StereoToMonoSampleProvider(samples);

        // Resample to 16 kHz
        var resampled = sourceFormat.SampleRate == 16000
            ? mono
            : new WdlResamplingSampleProvider(mono, 16000);

        var outputLength = (int)(bytesRecorded / (double)sourceFormat.BlockAlign
                                 * 16000 / sourceFormat.SampleRate * 2) + 1024;

        var output = new float[outputLength];
        var read = resampled.Read(output, 0, outputLength);
        return output[..read];
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Audio/Resampler.cs
git commit -m "feat(infra): add PCM Resampler (any format → 16kHz mono float)"
```

---

### Task 29: VoiceActivityDetector

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs`

- [ ] **Step 1: Implement**

Simple energy-threshold VAD that accumulates frames into speech windows. Whisper processes whole speech windows, not individual frames.

```csharp
// src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class VoiceActivityDetector
{
    private const float EnergyThreshold = 0.01f;
    private const int SilenceFramesToFlush = 20; // ~400ms at 50fps NAudio callbacks
    private const int MinFramesForSpeech = 5;

    public async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = new List<float>();
        var lastSpeaker = Domain.Sessions.Speaker.Other;
        int silenceCount = 0;
        int speechCount = 0;

        await foreach (var frame in frames.WithCancellation(ct))
        {
            var energy = frame.Samples.Select(s => s * s).DefaultIfEmpty().Average();
            bool isSpeech = energy > EnergyThreshold;

            if (isSpeech)
            {
                buffer.AddRange(frame.Samples);
                lastSpeaker = frame.Speaker;
                speechCount++;
                silenceCount = 0;
            }
            else if (buffer.Count > 0)
            {
                silenceCount++;
                if (silenceCount >= SilenceFramesToFlush && speechCount >= MinFramesForSpeech)
                {
                    yield return new SpeechWindow([.. buffer], lastSpeaker);
                    buffer.Clear();
                    speechCount = 0;
                    silenceCount = 0;
                }
                else if (silenceCount >= SilenceFramesToFlush)
                {
                    // Too short — discard noise
                    buffer.Clear();
                    speechCount = 0;
                    silenceCount = 0;
                }
            }
        }

        // Flush remaining buffer on stream end
        if (buffer.Count > 0 && speechCount >= MinFramesForSpeech)
            yield return new SpeechWindow([.. buffer], lastSpeaker);
    }
}

public sealed record SpeechWindow(float[] Samples, Domain.Sessions.Speaker Speaker);
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs
git commit -m "feat(infra): add energy-threshold VoiceActivityDetector"
```

---

### Task 30: NAudioCaptureService

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Audio/NAudioCaptureService.cs`

- [ ] **Step 1: Implement**

```csharp
// src/AIHelperNET.Infrastructure/Audio/NAudioCaptureService.cs
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class NAudioCaptureService : IAudioCaptureService
{
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        using var enumerator = new MMDeviceEnumerator();

        var micDevice = selection.MicDeviceId is not null
            ? enumerator.GetDevice(selection.MicDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        var loopbackDevice = selection.LoopbackDeviceId is not null
            ? enumerator.GetDevice(selection.LoopbackDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        using var mic = new WasapiCapture(micDevice);
        using var loopback = new WasapiLoopbackCapture(loopbackDevice);

        mic.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, mic.WaveFormat),
                    Speaker.Me,
                    DateTimeOffset.UtcNow));

        loopback.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, loopback.WaveFormat),
                    Speaker.Other,
                    DateTimeOffset.UtcNow));

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
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Register in DI**

Edit `src/AIHelperNET.Infrastructure/DependencyInjection.cs` — add inside `AddInfrastructure`:

```csharp
services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Audio/NAudioCaptureService.cs src/AIHelperNET.Infrastructure/DependencyInjection.cs
git commit -m "feat(infra): add NAudioCaptureService (WASAPI mic + loopback)"
```

---

### Task 31: WhisperModelProvider

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs`

- [ ] **Step 1: Implement**

Downloads models on first use and caches factories by size. Model files are GGML format stored in `%LOCALAPPDATA%\AIHelperNET\models\`.

```csharp
// src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Common;
using Whisper.net;
using Whisper.net.Ggml;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperModelProvider : IAsyncDisposable
{
    private readonly Dictionary<WhisperModelSize, WhisperFactory> _factories = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<WhisperFactory> GetFactoryAsync(WhisperModelSize size, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_factories.TryGetValue(size, out var existing)) return existing;

            var path = ModelPath(size);
            if (!File.Exists(path))
                await DownloadAsync(size, path, ct);

            var factory = WhisperFactory.FromPath(path);
            _factories[size] = factory;
            return factory;
        }
        finally
        {
            _lock.Release();
        }
    }

    private static string ModelPath(WhisperModelSize size)
    {
        var name = size switch
        {
            WhisperModelSize.Tiny   => "ggml-tiny.bin",
            WhisperModelSize.Base   => "ggml-base.bin",
            WhisperModelSize.Small  => "ggml-small.bin",
            WhisperModelSize.Medium => "ggml-medium.bin",
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };
        return Path.Combine(AppPaths.ModelsDir, name);
    }

    private static async Task DownloadAsync(WhisperModelSize size, string targetPath, CancellationToken ct)
    {
        var ggmlType = size switch
        {
            WhisperModelSize.Tiny   => GgmlType.Tiny,
            WhisperModelSize.Base   => GgmlType.Base,
            WhisperModelSize.Small  => GgmlType.Small,
            WhisperModelSize.Medium => GgmlType.Medium,
            _ => throw new ArgumentOutOfRangeException(nameof(size))
        };

        AppPaths.EnsureDirectoriesExist();
        await using var modelStream = await WhisperGgmlDownloader.GetGgmlModelAsync(ggmlType, cancellationToken: ct);
        await using var fileStream = File.Create(targetPath);
        await modelStream.CopyToAsync(fileStream, ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var f in _factories.Values) f.Dispose();
        _factories.Clear();
        _lock.Dispose();
        await ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs
git commit -m "feat(infra): add WhisperModelProvider with lazy download"
```

---

### Task 32: WhisperTranscriptionService

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`

- [ ] **Step 1: Implement**

Deduplicates adjacent transcript segments using `QuestionDetector.Jaccard` (threshold 0.85 — tighter than question dedup at 0.6).

```csharp
// src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(WhisperModelProvider models) : ITranscriptionService
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize modelSize,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var factory = await models.GetFactoryAsync(modelSize, ct);
        await using var processor = factory.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        var vad = new Audio.VoiceActivityDetector();
        string? lastEmitted = null;

        await foreach (var window in vad.AccumulateSpeechWindows(frames, ct))
        {
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                lastEmitted = seg.Text;
                yield return new TranscriptSegment(
                    seg.Text.Trim(),
                    window.Speaker,
                    DateTimeOffset.UtcNow,
                    seg.Probability);
            }
        }
    }

    private static bool IsNearDuplicate(string current, string? previous)
    {
        if (previous is null) return false;
        var a = Tokenize(current);
        var b = Tokenize(previous);
        return QuestionDetector.Jaccard(a, b) >= 0.85;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.Infrastructure/
```

- [ ] **Step 3: Register both in DI**

Edit `src/AIHelperNET.Infrastructure/DependencyInjection.cs` — add:

```csharp
services.AddSingleton<WhisperModelProvider>();
services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.Infrastructure/Transcription/ src/AIHelperNET.Infrastructure/DependencyInjection.cs
git commit -m "feat(infra): add WhisperTranscriptionService with VAD and near-duplicate filter"
```

---

**Phase 5 complete.** Continue with `2026-06-02-aihelper-phase6-ai-ocr.md`.
