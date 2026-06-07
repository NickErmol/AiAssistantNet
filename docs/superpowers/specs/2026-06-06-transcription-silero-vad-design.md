# Transcription — Silero VAD + Whisper Tuning Design Spec

**Date:** 2026-06-06
**Branch target:** `feature/transcription-silero-vad`
**Scope:** Replace energy-based VAD with Silero VAD, add per-window Whisper processor with rolling prompt and `WithNoContext()`, upgrade default model to Medium, add model-size settings UI

---

## Problem

Four issues affect the current transcription pipeline:

1. **Type-A hallucinations** — energy-based VAD triggers on background noise (HVAC, keyboard), sending silence/noise windows to Whisper, which invents text.
2. **Type-B repetitions** — single `WhisperProcessor` retained for the full session accumulates KV-cache context; Whisper echoes phrases from earlier in the session.
3. **Vocabulary drift** — static `InitialPrompt` is used for every window; Whisper gets no continuity signal between windows.
4. **Inaccurate default model** — `WhisperModelSize.Base` is fast but inaccurate for technical vocabulary; Medium is already pre-warmed at startup.

---

## Architecture Overview

The pipeline shape is unchanged. Two components are replaced or modified:

```
NAudioCaptureService
  → Channel<AudioFrame>
  → SileroVadDetector          ← replaces VoiceActivityDetector (same signature)
  → WhisperProcessor (per window, rolling prompt, WithNoContext)  ← modified
  → mergeChannel
  → TranscriptPipelineService
```

| Component | Status | Change |
|-----------|--------|--------|
| `NAudioCaptureService` | unchanged | — |
| `SessionRunner` | unchanged | — |
| `VoiceActivityDetector` | **deleted** | replaced by `SileroVadDetector` |
| `SileroVadDetector` | **new** | same static `AccumulateSpeechWindows` signature |
| `SileroModelProvider` | **new** | downloads + caches `silero_vad.onnx` |
| `WhisperTranscriptionService` | **modified** | per-window processor, rolling prompt, `WithNoContext()` |
| `WhisperModelProvider` | unchanged | already has LargeTurbo/Large |
| `TranscriptPipelineService` | unchanged | — |
| `ITranscriptionService` | unchanged | — |

---

## Section 1: Silero VAD

### Model

Silero VAD v4 ONNX (`silero_vad.onnx`, ~1.8 MB, MIT license). Stateful LSTM: each 512-sample chunk (32 ms @ 16 kHz) updates hidden state tensors `h` and `c` (shape `[2,1,64]`). Output: single float speech probability 0–1.

### `SileroModelProvider`

Singleton. Mirrors `WhisperModelProvider` pattern:

```csharp
public sealed class SileroModelProvider : IAsyncDisposable
{
    private InferenceSession? _session;
    private readonly SemaphoreSlim _lock = new(1, 1);

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
        finally { _lock.Release(); }
    }

    // DownloadAsync: fetch from Silero GitHub releases to AppPaths.ModelsDir
    // DisposeAsync: dispose InferenceSession, _lock
}
```

### `SileroVadDetector`

Static class in `Infrastructure/Audio/`. Same output type (`SpeechWindow`) as the deleted `VoiceActivityDetector`.

```csharp
public static class SileroVadDetector
{
    public static async IAsyncEnumerable<SpeechWindow> AccumulateSpeechWindows(
        IAsyncEnumerable<AudioFrame> frames,
        SileroModelProvider modelProvider,
        [EnumeratorCancellation] CancellationToken ct)
}
```

#### Processing loop

Audio frames are split into 512-sample chunks. Each chunk is fed to the ONNX session; LSTM state (`h`, `c`) is carried between chunks and reset to zeros at each new speech window boundary.

#### Hysteresis gating

```
speech start  : prob ≥ 0.50 for 2 consecutive chunks
speech continue: prob ≥ 0.35
flush trigger : 12 consecutive sub-threshold chunks (~375 ms silence)
```

#### Window constraints

| Constraint | Value | Rationale |
|-----------|-------|-----------|
| Min chunks | 8 (~250 ms) | Discards noise bursts |
| Max chunks | 240 (~7.5 s) | Force-flushes long answers |

---

## Section 2: Whisper Changes

Three changes to `WhisperTranscriptionService`:

### Per-window processor rebuild

Current code builds one `WhisperProcessor` when `processor is null` and reuses it for the session lifetime. New code builds and `await using`-disposes one processor per speech window.

### `WithNoContext()`

Prevents Whisper from carrying internal KV-cache context between windows. Eliminates type-B repetitions.

### Rolling prompt

`lastEmitted` (already tracked for near-dup filtering) is passed as the prompt for each window. Falls back to static `InitialPrompt` until the first segment is emitted.

```csharp
await foreach (var window in SileroVadDetector.AccumulateSpeechWindows(frames, _modelProvider, ct))
{
    await _buildLock.WaitAsync(ct);
    WhisperProcessor processor;
    try
    {
        processor = factory.CreateBuilder()
            .WithLanguage(lang ?? "en")
            .WithTemperature(0)
            .WithNoContext()
            .WithPrompt(lastEmitted ?? InitialPrompt)
            .WithNoSpeechThreshold(0.6f)
            .WithSingleSegment()
            .Build();
    }
    finally { _buildLock.Release(); }

    await using var _ = (IAsyncDisposable)processor;

    await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
    {
        // existing filtering: blank, hallucination phrases, min-words, near-dup
        lastEmitted = seg.Text.Trim();
        yield return new TranscriptSegment(lastEmitted, window.Speaker, DateTimeOffset.UtcNow, seg.Probability);
    }
}
```

`_buildLock` (static `SemaphoreSlim(1)`) is retained — still serialises concurrent mic and loopback builds to prevent KV-cache allocation stalls.

`IsNearDuplicate` filter is kept as defence-in-depth.

### Constructor change

`WhisperTranscriptionService` gains `SileroModelProvider` as a second constructor parameter (injected from DI).

---

## Section 3: Default Model → Medium

Two fallback sites updated from `WhisperModelSize.Base` to `WhisperModelSize.Medium`:

| File | Site |
|------|------|
| `Infrastructure/Persistence/JsonSettingsStore.cs` | `DefaultSettings()` |
| `App/ViewModels/SettingsViewModel.cs` | Null-fallback on load |

Medium is already pre-warmed at `App.OnStartup` — no download friction for existing installs.

---

## Section 4: Dependencies + Model Bootstrapping

### New NuGet

`Microsoft.ML.OnnxRuntime` added to `AIHelperNET.Infrastructure.csproj` only.

### DI Registration

`SileroModelProvider` registered as a singleton in `InfrastructureServiceExtensions` alongside `WhisperModelProvider`.

### Pre-warm in `App.OnStartup`

```csharp
// existing
_ = Task.Run(() => whisperModels.GetFactoryAsync(WhisperModelSize.Medium, ct));
// new
_ = Task.Run(() => sileroModels.GetSessionAsync(ct));
```

### Settings UI — Model Size Picker

Added to the TRANSCRIPTION section of `SettingsWindow.xaml`, before the Language picker.

**`EnumValues.cs`:**
```csharp
public static IReadOnlyList<EnumOption<WhisperModelSize>> WhisperModelSizes { get; } =
[
    new("Tiny",        WhisperModelSize.Tiny),
    new("Base",        WhisperModelSize.Base),
    new("Small",       WhisperModelSize.Small),
    new("Medium",      WhisperModelSize.Medium),
    new("Large Turbo", WhisperModelSize.LargeTurbo),
    new("Large",       WhisperModelSize.Large),
];
```

**`SettingsViewModel.cs`:** Add `WhisperModel` observable property; load from `s.WhisperModel` in `LoadAsync`; save in `SaveSettings`.

**`SettingsWindow.xaml`:** `ComboBox` bound to `WhisperModel` via `SelectedValue`/`SelectedValuePath="Value"` using `EnumValues.WhisperModelSizes`.

---

## Section 5: Testing

### `SileroVadHysteresisTests` (new, `AIHelperNET.Infrastructure.Tests`)

Extract the gating state machine into a pure helper accepting `float[] probabilities` → `SpeechWindow[]`. No ONNX model required. Tests run in CI.

| Test | Input | Expected |
|------|-------|----------|
| All silence | prob = 0.1 throughout | No windows emitted |
| Short burst below min | ≥ 0.5 for 4 chunks, then silence | Window discarded |
| Normal speech | ≥ 0.5 for 20 chunks, then silence | One `SpeechWindow` |
| Max-window force-flush | ≥ 0.5 for 241 chunks | Two windows |
| Noisy onset | alternates 0.4/0.6 for 2 chunks | No speech start (requires 2 consecutive ≥ 0.5) |

### `SileroVadDetectorTests` (conditional, `AIHelperNET.Infrastructure.Tests`)

`[SkippableFact]` — skip if `silero_vad.onnx` absent. Run on dev machine with synthetic audio buffers (sine wave as speech signal, silence as noise).

### `SessionRunnerTests`

Existing tests unchanged — they use `FakeTranscriptionService` and are not affected by the VAD change.

### Manual E2E

After implementation, before PR:
- Start session in AudioAndScreen mode
- Verify mic transcript appears (Speaker.Me)
- Verify loopback transcript appears (Speaker.Other)
- Verify no repetition hallucinations during sustained speech
- Verify all settings load/save correctly (model size picker, language picker)

---

## Files Summary

| File | Change |
|------|--------|
| `Infrastructure/Audio/VoiceActivityDetector.cs` | **Deleted** |
| `Infrastructure/Audio/SileroVadDetector.cs` | **New** |
| `Infrastructure/Transcription/SileroModelProvider.cs` | **New** |
| `Infrastructure/Transcription/WhisperTranscriptionService.cs` | Per-window processor, `WithNoContext()`, rolling prompt, `SileroModelProvider` param |
| `Infrastructure/Persistence/JsonSettingsStore.cs` | Default model Base → Medium |
| `Infrastructure/DI/InfrastructureServiceExtensions.cs` | Register `SileroModelProvider` singleton |
| `AIHelperNET.Infrastructure.csproj` | Add `Microsoft.ML.OnnxRuntime` NuGet |
| `App/App.xaml.cs` | Pre-warm `SileroModelProvider` at startup |
| `App/ViewModels/SettingsViewModel.cs` | Add `WhisperModel` property, fix fallback |
| `App/Windows/EnumValues.cs` | Add `WhisperModelSizes` list |
| `App/Windows/SettingsWindow.xaml` | Add model `ComboBox` under TRANSCRIPTION |
| `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs` | **New** |
| `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadDetectorTests.cs` | **New** (skippable) |

---

## Out of Scope

- Whisper token-level streaming / partial word display
- Silero Large pre-warm at startup
- Any changes to question detection or answer generation
- Speaker diarization beyond the current mic/loopback channel split
