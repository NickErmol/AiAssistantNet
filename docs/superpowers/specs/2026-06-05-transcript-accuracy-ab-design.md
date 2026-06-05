# Transcript Accuracy — A+B Design Spec

**Date:** 2026-06-05  
**Branch target:** `feature/transcript-accuracy-ab`  
**Scope:** Rolling Whisper prompt, VAD window tuning, default model upgrade to Medium, Large-v3 model support

---

## Problem

Two accuracy problems reported affecting both mic and loopback audio:

1. **Wrong words** — Whisper misrecognises technical vocabulary because each speech window is decoded without context from the previous one.
2. **Phrases cut in the middle** — The 4-second VAD window cap splits long interview questions before Whisper sees the full sentence.

---

## Change 1: Rolling Whisper Prompt

### Current behaviour

`WhisperTranscriptionService` creates a single `WhisperProcessor` for the session lifetime, built with a static `InitialPrompt`:

```
"Technical interview. Software engineering, system design, algorithms, data structures, coding."
```

Whisper's prompt token mechanism is designed to inject vocabulary context from the previous inference into the next. The static prompt wastes this mechanism.

### New behaviour

The processor is built inside the speech-window loop. Each window receives the last emitted segment text as its prompt. If no segment has been emitted yet the static `InitialPrompt` is used as the initial seed.

```
window 1 → prompt: InitialPrompt
window 2 → prompt: "Can you implement a binary search tree?"
window 3 → prompt: "And what is the time complexity of insertion?"
```

### Implementation notes

- `lastEmitted` is already tracked (used for near-duplicate filtering). Reuse it as the prompt value.
- `await using` the new processor inside the loop; the factory (model weights) remains loaded across all windows.
- No change to `ITranscriptionService` interface.

### Files

| File | Change |
|------|--------|
| `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Move processor build inside window loop; pass `lastEmitted ?? InitialPrompt` |

---

## Change 2: VAD Window Tuning

### Current constants

```csharp
const int SilenceFramesToFlush = 6;   // ~600 ms
const int MaxWindowFrames      = 40;  // ~4 s
```

### New constants

```csharp
const int SilenceFramesToFlush = 4;   // ~400 ms — flush sooner on real silence
const int MaxWindowFrames      = 80;  // ~8 s — fits full interview questions
```

`MinFramesForSpeech` stays at 8 (~800 ms) — still blocks noise bursts.

**Rationale:** A typical interview question runs 5–8 seconds. The old 4-second cap forces a mid-sentence flush. Reducing the silence threshold from 600 ms to 400 ms compensates by flushing more promptly at genuine pauses, so individual clauses still land as separate windows rather than accumulating into one 8-second blob.

### Files

| File | Change |
|------|--------|
| `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs` | Update two constants |

---

## Change 3: Default Model → Medium

### Current state

Two code sites hardcode `WhisperModelSize.Base` as the fallback:

- `JsonSettingsStore.DefaultSettings()` — used on first run or missing settings file
- `SettingsViewModel.cs:125` — used when saved settings have a null `WhisperModel`

Medium is already pre-warmed at app startup (`App.xaml.cs:123`), so upgrading the default adds no download friction for existing installs.

### New state

Both sites use `WhisperModelSize.Medium`.

### Files

| File | Change |
|------|--------|
| `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs` | `DefaultSettings()`: Base → Medium |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs:125` | Fallback: Base → Medium |

---

## Change 4: Large-v3 Model Support

### Enum

Add `LargeTurbo` and `Large` to `WhisperModelSize` in `AudioFrame.cs`:

```csharp
public enum WhisperModelSize { Tiny, Base, Small, Medium, LargeTurbo, Large }
```

- **LargeTurbo** maps to `GgmlType.LargeV3Turbo` — ~800 MB, 8× faster than full Large, retains most accuracy. Best practical choice for real-time use.
- **Large** maps to `GgmlType.LargeV3` — ~3 GB, maximum accuracy.

### Model provider

Add both variants to both switch arms in `WhisperModelProvider`:

```csharp
// ModelPath
WhisperModelSize.LargeTurbo => "ggml-large-v3-turbo.bin"
WhisperModelSize.Large      => "ggml-large-v3.bin"

// DownloadAsync
WhisperModelSize.LargeTurbo => GgmlType.LargeV3Turbo
WhisperModelSize.Large      => GgmlType.LargeV3
```

No automatic pre-warm for either. Download happens on first session start when the model is selected.

### Settings UI

The TRANSCRIPTION section in `SettingsWindow.xaml` currently shows only a Language picker — no model size selector exists. This change adds one.

**`EnumValues.cs`** — add a static list:

```csharp
public static IReadOnlyList<EnumOption<WhisperModelSize>> WhisperModelSizes { get; } =
[
    new("Tiny",         WhisperModelSize.Tiny),
    new("Base",         WhisperModelSize.Base),
    new("Small",        WhisperModelSize.Small),
    new("Medium",       WhisperModelSize.Medium),
    new("Large Turbo",  WhisperModelSize.LargeTurbo),
    new("Large",        WhisperModelSize.Large),
];
```

**`SettingsViewModel.cs`** — add `WhisperModel` property, load from `s.WhisperModel` in `LoadAsync`, use in `SaveSettings` instead of the current `current?.WhisperModel ?? WhisperModelSize.Base`.

**`SettingsWindow.xaml`** — add a ComboBox under TRANSCRIPTION, before the Language picker, bound to `WhisperModel` via `SelectedValue`/`SelectedValuePath="Value"` using `EnumValues.WhisperModelSizes`.

### Files

| File | Change |
|------|--------|
| `src/AIHelperNET.Application/Abstractions/AudioFrame.cs` | Add `Large` enum value |
| `src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs` | Two switch arms for Large |
| `src/AIHelperNET.App/Windows/EnumValues.cs` | Add `WhisperModelSizes` list |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` | Add `WhisperModel` property, wire load/save |
| `src/AIHelperNET.App/Windows/SettingsWindow.xaml` | Add model ComboBox under TRANSCRIPTION |

---

## Files Summary

| File | Change |
|------|--------|
| `AIHelperNET.Application/Abstractions/AudioFrame.cs` | Add `Large` to enum |
| `AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Rolling prompt |
| `AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs` | Large-v3 paths |
| `AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs` | Window constants |
| `AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs` | Default → Medium |
| `AIHelperNET.App/ViewModels/SettingsViewModel.cs` | WhisperModel property + fallback |
| `AIHelperNET.App/Windows/EnumValues.cs` | WhisperModelSizes list |
| `AIHelperNET.App/Windows/SettingsWindow.xaml` | Model ComboBox |

---

## Out of Scope

- Silero-VAD replacement (planned as a follow-up feature)
- Whisper Large pre-warm at startup
- Any changes to question detection or answer generation
