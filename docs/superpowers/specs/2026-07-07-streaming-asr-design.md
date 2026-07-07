# Optional Streaming ASR (Deepgram) — Design

**Date:** 2026-07-07
**Backlog item:** #7 (improvement-proposal-2026-07-06.md) — "Optional streaming ASR provider"
**Status:** Approved

## Goal

Cut speech→transcript latency from ~3.5 s (batch Whisper LargeTurbo, paid after the VAD
window closes) to sub-second, behind an opt-in setting. Local Whisper stays the privacy
default and the offline fallback. Only finalized results are consumed — no interim results
anywhere in the pipeline or UI.

### Non-goals

- **Interim results / live captions.** v1 is finals-only; Deepgram's endpointed finals
  (~0.3–1 s after a pause) already deliver the latency win without touching the pipeline
  or transcript UI. Partials are a possible v2 layer.
- **AssemblyAI or other providers.** Deepgram only; the seam makes a second provider easy
  later if wanted.
- **Onset pre-roll (#8).** Dropped on evidence: the 2026-06-17 latency work showed
  sentence-initial words are correct on LargeTurbo (the current default) — the
  dropped-first-word defect was Small/Medium model capacity, not clipped audio onset.
  Revisit only if Small/Medium becomes a supported daily driver.

## Context (current pipeline)

`NAudioCaptureService` → per-speaker channels (mic = `Speaker.Me`, loopback =
`Speaker.Other`) → two independent `ITranscriptionService.TranscribeAsync` streams
(WhisperTranscriptionService, with Silero VAD windowing inside) → merge channel →
`TranscriptPipelineService`. Frames reaching `TranscribeAsync` are already 16 kHz mono
`float[]` (`Resampler`). Each `TranscribeAsync` call receives a single-speaker stream by
construction.

## Design

### 1. Settings & secrets

- New `SttProvider` enum: `Whisper` (default) | `Deepgram`. Stored on `AppSettingsDto` in
  settings.json — no DB migration.
- `ISecretStore` generalizes from a single implicit Anthropic key to named secrets:
  `SecretKind` enum (`Anthropic` | `Deepgram`) parameter on
  `SaveApiKey` / `GetApiKey` / `DeleteApiKey` / `HasApiKey`.
  `WindowsCredentialSecretStore` maps each kind to its own credential-manager target;
  existing Anthropic call sites migrate mechanically. SecureString discipline unchanged
  (`SecureStringHelpers`).
- Settings → Transcription section: provider dropdown + Deepgram API key field (visible
  only when Deepgram is selected), reusing the existing key-entry UX. A privacy note
  states that cloud STT sends both mic and interviewer audio to Deepgram.

### 2. Interface refactor

`ITranscriptionService.TranscribeAsync(frames, WhisperModelSize, language,
glossaryDomains, ct)` is Whisper-flavored. Fold the per-session parameters into a record:

```csharp
public sealed record TranscriptionOptions(
    WhisperModelSize Model,          // used by Whisper; ignored by Deepgram
    string Language,                 // BCP-47 or "auto"
    IReadOnlySet<string> GlossaryDomains);

IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
    IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options, CancellationToken ct);
```

Mechanical change at SessionRunner (two call sites), WhisperTranscriptionService, and
test fakes.

### 3. DeepgramTranscriptionService (Infrastructure/Transcription)

- One WebSocket connection per `TranscribeAsync` call; SessionRunner's two per-speaker
  streams naturally become two connections (each billed as mono audio).
- Audio: `float[]` → 16-bit little-endian PCM. Connection params:
  `model=nova-3, encoding=linear16, sample_rate=16000, channels=1, smart_format=true,
  interim_results=false`, `language` from options (omit for `auto`), glossary terms
  mapped to `keyterm` boosting via the existing `ITranscriptionGlossaryProvider`
  (capped to Deepgram's per-request limit; overflow logged once).
- Emission: yield one `TranscriptSegment` per result with `speech_final=true` —
  text from `alternatives[0].transcript` (skip empty), confidence from
  `alternatives[0].confidence`, `Speaker` from the input stream, `CapturedAt` mapped from
  Deepgram's stream-relative `start` seconds against the wall-clock time the connection
  started streaming. If timestamp mapping is unavailable, fall back to receive-time wall
  clock (equivalent to today's effective precision).
- Lifecycle: send a `KeepAlive` text frame during silence gaps (idle > ~5 s of no audio
  writes) so Deepgram doesn't close the socket; on input completion send `CloseStream`
  and drain remaining finals before completing the enumerable.
- All socket I/O goes through a thin `IDeepgramSocket` / factory seam (wrapping
  `ClientWebSocket`) so unit tests run against a scripted fake. **No Deepgram SDK
  dependency** — the protocol is small (binary PCM out, JSON in) and owning it keeps
  reconnect/fallback semantics in our hands.

### 4. Resilience & fallback (ResilientTranscriptionService)

Wrapper implementing `ITranscriptionService`, composed of the Deepgram service and the
Whisper service:

- Input frames are pumped into an internal channel; the active provider consumes from it.
- On WS open failure **or** mid-stream fault: one reconnect attempt (fresh connection);
  if that fails, permanent per-session fallback to Whisper.
- Unconsumed frames remain queued in the channel, so the Whisper takeover resumes from
  the outage point — no audio is dropped across the switch.
- Whisper's model is preloaded at session start even when Deepgram is selected, so a
  mid-interview fallback never stalls on the ~800 MB model load.
- Fallback surfaces as a Serilog warning + a notice on the overlay header status text
  (`Header_StatusText`), e.g. "STT: using local Whisper (Deepgram unavailable)".

### 5. Resolution & DI

`ISttResolver.Resolve(SttProvider) → ITranscriptionService`, mirroring
`IAnswerProviderResolver`:

- `Whisper` → existing `WhisperTranscriptionService`.
- `Deepgram` → `ResilientTranscriptionService(deepgram, whisper)`.
- `Deepgram` selected but no API key stored → resolve directly to Whisper and raise the
  same status notice (session start never blocks on a missing key).

SessionRunner resolves the provider at `StartAsync` (it already loads settings there for
the answer-provider warm-up).

## Testing

All defaults per repo rules: no real API calls in the standard suites; local suites are
the merge gate (no CI).

- **Unit (fake socket):** Deepgram JSON → segment parsing (`speech_final` gating,
  empty-transcript skip, confidence, timestamp mapping); float→PCM conversion;
  KeepAlive/CloseStream lifecycle; keyterm cap.
- **Fallback state machine:** open-fail → reconnect-fail → Whisper continuation with no
  frame loss; mid-stream fault → reconnect-success continues on Deepgram; second fault →
  permanent Whisper; missing key → straight to Whisper.
- **Refactor regression:** existing Application/Integration suites green after the
  `TranscriptionOptions` change (they use fake transcription services).
- **Live eval (opt-in, env-gated, key from Credential Manager — mirrors the existing
  live-eval pattern):** stream a fixture WAV over the real WS, assert expected phrases
  appear and first `speech_final` latency is under a threshold.

## Risks

- **Glossary size vs `keyterm` limits** — cap and log; boosting is best-effort.
- **`CapturedAt` accuracy** — question-fold/split-guard windows use item timestamps;
  stream-relative mapping should be *more* accurate than today's window-close stamping,
  with wall-clock receive time as the floor.
- **Long-silence socket closes** — covered by KeepAlive; a close that slips through is
  just a mid-stream fault → reconnect → fallback path.
- **Privacy** — cloud STT is opt-in, default stays Whisper, and the settings UI says
  what leaves the machine.
