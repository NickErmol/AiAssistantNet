# Transcription Latency — Instrument, Tune, De-cost

**Date:** 2026-06-17
**Status:** Design (approved in brainstorming, pending written-spec review)
**Branch:** `feature/transcription-latency`

## Problem

Live transcription feels slow. Two symptoms:

1. **Large-turbo is unusable on this hardware.** Selecting `LargeTurbo` (809M params,
   `ggml-large-v3-turbo.bin`) produces a transcript only **10–15s after the sentence
   finished**, forcing the user back to the `Small` model.
2. **The pipeline is batch-per-window, not streaming.** A transcript segment appears only
   after a speech window completes (after ~375ms of trailing silence, or the 7.5s
   force-flush), then the *whole* window is decoded in one Whisper pass. On long continuous
   speech nothing shows until the force-flush — a "nothing's happening" dead-air feeling.

### Why true streaming is out of scope

Whisper is a 30-second encoder-decoder model with **no incremental/partial-result API**
(verified against Whisper.net 1.9.1: the prompt is build-time only; `ProcessAsync` takes only
audio + a cancellation token). "Streaming" with Whisper means re-decoding a growing buffer
repeatedly — multiplying GPU load and fighting Whisper's instability on short partial buffers.
We explicitly reject that here. We also reject swapping to a streaming ASR engine (e.g. Vosk)
and beam search (both add latency or complexity). This is a **middle-ground latency** effort.

### Diagnostic findings (from code + logs)

- **No CPU fallback** in recent logs (`D:\AIHelperNET\logs\log-2026061*.txt`) — Vulkan *is*
  loading. The 10–15s is Vulkan being genuinely slow on the Iris Xe integrated GPU for an
  809M model (shared system RAM), **not** a silent CPU fallback.
- **No timing instrumentation exists** in the transcription path — we are currently guessing
  where the seconds go.
- **A fresh `WhisperProcessor` is rebuilt every window** (`WhisperTranscriptionService`,
  `factory.CreateBuilder()…Build()` inside the window loop), *specifically* to inject the
  rolling-context prompt. For large models that `Build()` reallocates the KV cache each time —
  a suspected but unmeasured contributor. (The factory holds the model; `Build()` does **not**
  reload it.)
- **Windows can be up to 7.5s** of audio (`VadWindowAccumulator.MaxChunks = 240`), so the
  worst case is large model × longest window.

## Goal

Reduce perceived transcription latency and explain the large-turbo slowness, in **dependency
order**: measure first, then tune what the measurements point at. Do not "spend" any accuracy
tradeoff unless the measurements justify it.

## Design

Three components, executed in order. Component 1 ships first and gates Component 3.

### Component 1 — Timing instrumentation (build first, ship first)

In `WhisperTranscriptionService.TranscribeAsync`, time the two costs **separately** with a
`Stopwatch`:

- **build** — `factory.CreateBuilder()…Build()` (KV-cache allocation)
- **inference** — the `processor.ProcessAsync` loop over the window

Emit **one log line per window** at **Information** level, **metadata only** (no transcript
text, no prompt text — stays within the standing security rule "don't log raw
transcript/prompt at Information"):

| Field | Meaning |
|---|---|
| `model` | `WhisperModelSize` in use |
| `speaker` | `Me` / `Other` |
| `windowAudioSec` | `window.Samples.Length / 16000f` |
| `buildMs` | processor build time |
| `inferMs` | inference time |
| `rtf` | real-time factor = `inferMs / (windowAudioSec * 1000)` |

`rtf > 1` means slower-than-real-time — the direct measure of the 10–15s problem. The
**build-vs-infer split** decides whether Component 3 is worth doing at all.

This component is purely additive (logging only) and carries no behavior change.

### Component 2 — VAD windowing tuning (`VadWindowAccumulator` constants)

Adjust two constants (starting points; finalized empirically against Component 1 data + a
real-audio A/B pass):

| Constant | Current | New (start) | Effect |
|---|---|---|---|
| `MaxChunks` | 240 (~7.5s) | ~112 (~3.5s) | Long speech emits as a sequence of ~3.5s **final** chunks instead of one 7.5s block — removes dead air, no text rewriting. |
| `SilenceFlushCount` | 12 (~375ms) | 8 (~250ms) | Finals surface ~125ms sooner after a pause. Risk: clipping trailing words — hence empirical tuning. |

`StartConfirmCount` and `MinChunks` are unchanged (false-start and too-short-window guards).

Smaller windows mean more, shorter inference calls — per-final latency drops; total GPU work
is roughly unchanged. Splitting a long sentence across windows is acceptable: the question
boundary detector already re-assembles multi-window questions, so the effect is mostly
cosmetic in the transcript pane.

### Component 3 — Per-window rebuild cost (conditional on Component 1)

The per-window rebuild exists only to refresh the rolling-context prompt (the last ~50 words,
fed to Whisper to improve continuity of names/jargon across pauses). The Whisper.net API
forces a rebuild to change the prompt.

**Decision rule, driven by Component 1's `buildMs`:**

- **If `buildMs` is significant** (rebuild is a real share of latency on the large model):
  build the processor **once per stream** with a *stable* prompt (`InitialPrompt` + glossary
  suffix), **drop the per-window rolling-context prompt**, and rebuild only when the glossary
  domain set changes. Reuse the processor across windows for everything else.
  - **Tradeoff (accepted):** lose the last-~50-words continuity bias. Mitigated because
    `WithNoContext()` already makes each call independent, the hallucination/near-duplicate
    filters remain, and the **glossary** domain bias (the more important note) survives.
  - User decision: **"drop the note if build is expensive."**
- **If `buildMs` is negligible:** do nothing here — inference dominates, and the only real
  levers are model size + smaller windows (Component 2).

The processor is reused across sequential `ProcessAsync` calls within a single stream's
`await foreach` (calls are already serialized there). Each stream (mic, loopback) keeps its
own processor; the static `_buildLock` still guards the now-rare builds.

## Out of scope (YAGNI)

- Full sliding-window pseudo-streaming / revising interim partials.
- Swapping to a streaming ASR engine (Vosk, RNN-T).
- Beam search (accuracy-for-latency in the wrong direction here).
- A compute-device selector UI / CUDA path.

## Testing

- **Unit:** update existing `VadWindowAccumulator` tests for the new window-size constants
  (expected chunk counts / flush points). If Component 3 lands, add a test asserting the
  processor is reused (built once) when the prompt is unchanged, and rebuilt when the glossary
  domain set changes.
- **Manual A/B (real audio):** drive questions via TTS → render device → WASAPI loopback (the
  established `Speaker.Other` technique). Compare:
  - `Small` vs `LargeTurbo` **RTF** straight from the new Component 1 logs.
  - Perceived responsiveness before/after Component 2 (dead-air feel on long speech).
  - Transcript accuracy with rolling-context prompt on vs off, if Component 3 drops it.
- **Gotcha:** stop the running overlay app before rebuilding the App project (it locks output
  DLLs). VAD/window settings and model selection are read at session start.

## Files touched

| File | Change |
|---|---|
| `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Component 1 timing + log; Component 3 processor reuse (conditional) |
| `src/AIHelperNET.Infrastructure/Audio/VadWindowAccumulator.cs` | Component 2 constants |
| `tests/AIHelperNET.Infrastructure.Tests/Audio/SileroVadHysteresisTests.cs` | Update expected window sizes for new constants; add processor-reuse test |

## Rollout order

1. Component 1 (instrumentation) → run a live session → read RTF + build/infer split.
2. Component 2 (VAD constants) → tune against the numbers + manual A/B.
3. Component 3 **only if** `buildMs` proved significant in step 1.
