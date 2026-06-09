# Real-Audio E2E Suite + Answer-Backend Toggle — Design

**Date:** 2026-06-09
**Status:** Approved (brainstorming) — pending spec review
**Branch target:** `feature/real-audio-e2e` → `develop`

## Summary

Two related deliverables:

1. **Settings toggle (Part A):** expose the already-plumbed `AiBackend` choice
   (Claude / Ollama) in the Settings UI so the user can switch answer providers at runtime.
2. **Real-audio E2E suite (Part B/C):** a higher-fidelity "Tier C" end-to-end test that drives
   the real `SessionRunner` audio loop with **real Whisper transcription** over **pre-recorded WAV
   fixtures**, tagged per speaker (`Me` = mic, `Other` = system), asserting both the transcript and
   the resulting answer card for a set of conversation scenarios — plus one gated live-answer smoke
   test that hits **real Ollama**.

This closes the single biggest coverage gap: today's `SessionRunnerAudioE2ETests` (Tier B) swap in a
`ScriptedTranscriptionService`, so the **real Whisper path** (audio → VAD → transcript) is never
exercised end-to-end and would silently break on a model/runtime upgrade.

## Background / current state

- `SessionRunner` (`src/AIHelperNET.App/Services/SessionRunner.cs`) fans NAudio frames into two
  per-speaker channels: `Speaker.Me` (mic / `WasapiCapture`) and `Speaker.Other`
  (system / `WasapiLoopbackCapture`). Each channel feeds an independent VAD+Whisper instance; the
  consumer greedily merges consecutive same-speaker fragments within `segmentMergeWindowMs` into one
  transcript item before question detection fires.
- The answer backend is **already plumbed end-to-end**: `AppSettingsDto.ActiveBackend`,
  `AnswerProviderResolver.Resolve(backend)` → `ClaudeAnswerProvider` / `OllamaAnswerProvider`, and
  all four answer commands call `providerResolver.Resolve(settings.ActiveBackend)`. The **only**
  missing piece is a UI control: `SettingsViewModel` preserves the existing value on save but never
  lets the user change it.
- `InterviewHost` (test host) wires `ITranscriptSink` as a no-op `Substitute`, so transcript text is
  not currently surfaced in any test.

## Part A — Answer-backend toggle (Claude / Ollama)

Pure UI + ViewModel change; no Domain/Application/Infrastructure change, **no EF migration** (settings
live in JSON via `JsonSettingsStore`, not the database).

- **`SettingsViewModel`:**
  - Add `[ObservableProperty] private AiBackend _activeBackend = AiBackend.Claude;`
  - In `LoadAsync`: `ActiveBackend = s.ActiveBackend;`
  - On save: pass `ActiveBackend` into the `AppSettingsDto` (replacing the
    `current?.ActiveBackend ?? AiBackend.Claude` pass-through).
- **`SettingsWindow.xaml`:** two `RadioButton`s (Claude / Ollama) on the **API Key tab** (provider
  selection sits naturally beside the key field), bound to `ActiveBackend` through an enum↔bool
  converter (one converter parameterised by the enum member, following existing converter patterns in
  the project).
- **Tests:** a ViewModel test asserting load reflects the persisted backend and save round-trips the
  selected backend into the `SaveSettingsCommand` payload.

## Part B — Real-audio E2E suite (Tier C)

Lives in `tests/AIHelperNET.Integration.Tests/E2E/`, reusing `InterviewHost` with two overrides.

### Audio injection mechanism

- **`WavFileAudioCaptureService : IAudioCaptureService`** — a test capture service that, given an
  ordered script of `(Speaker, wavFixturePath, gapMsBefore)`, reads each WAV, resamples to 16 kHz
  mono float (reusing the production `Resampler`), and yields `AudioFrame`s tagged with the
  scripted `Speaker` and realistic timestamps. After the last utterance the stream completes so the
  `SessionRunner` loop drains naturally (same contract as the existing `ScriptedAudioCaptureService`).
- **Real Whisper:** the real `ITranscriptionService` (Whisper.net) runs unchanged on the injected
  real audio — this is the path under test. Model size: **Medium** (chosen for transcription
  reliability over speed).
- The NAudio device layer (`WasapiCapture`/`WasapiLoopbackCapture`) is the only seam bypassed — it is
  a thin WASAPI adapter and is not meaningfully automatable without real hardware.

### Transcript visibility

- Replace the no-op `ITranscriptSink` in the test host with a **`CapturingTranscriptSink`** that
  records `(Speaker, Text)` lines. Each scenario asserts the per-speaker transcript — directly
  fulfilling "see audio transcript from Me and Other."

### Answer provider

- The scenario tests use the existing deterministic **`FakeAnswerProvider`** (echoes the prompt), so
  routing/structure assertions are stable. (Real-LLM answers are covered separately in Part C.)

### WAV fixtures

- **TTS-generated**, committed under `tests/AIHelperNET.Integration.Tests/Fixtures/audio/`.
- A committed, re-runnable generator (`tools/generate-audio-fixtures.ps1`, Windows SAPI
  `System.Speech` → 16 kHz mono WAV) produces one WAV per scripted line from a text manifest, so
  fixtures are reproducible and reviewable rather than opaque binaries with unknown provenance.
- Fixture text mirrors the existing scripted-interview lines (e.g. "What is dependency injection?").

### Scenarios

Each plays real audio on the relevant channel and asserts transcript + card outcome:

| # | Audio (channel) | Expected |
|---|---|---|
| 1 | Other: "What is dependency injection?" | 1 transcript line (Other) → **1** turn/card |
| 2 | Other in two fragments within merge window: "What is dependency…" + "…injection?" | fragments **merge** → 1 transcript line → **1** turn/card (double-card regression guard) |
| 3 | Other question → card; then Me: "do you mean constructor injection specifically?" | Me line attaches as clarifier → **same** turn refines (≥2 answer versions); card text folds clarification |
| 4 | Other question → card; then Other: "also keep it short" | Rule-8 regeneration → **same** turn refines (≥2 versions) |
| 5 | Other: "What is dependency injection?" then "Now explain CQRS" | **2** turns/cards |
| 6 | Me: non-question chit-chat (no preceding Other) | **no** standalone turn/card created |

Scenarios mirror the deterministic Tier-A/B coverage but exercise the real Whisper path. Whisper
output is asserted **fuzzily** (case-insensitive `Contain` on key tokens), never by exact string,
since transcription is not bit-deterministic.

### Test category / runtime

- Real Whisper + Medium model makes these tests **slow** and dependent on the Medium model being
  present (downloaded on first use by `WhisperModelProvider`). Tag them with
  `[Trait("Category", "RealAudio")]` so the default `dotnet test` run can **exclude** them
  (`--filter "Category!=RealAudio"`), and run them explicitly in a dedicated/slower CI lane.
- Document the one-time Medium model download and the fixture-generation step in the test project /
  E2E skill.

## Part C — Gated live-answer smoke test

One test that exercises the **real Ollama** answer provider end-to-end (real `PromptBuilderService`
→ `OllamaAnswerProvider` → streaming sink):

- **Gated/skippable:** skips automatically when the local Ollama endpoint is unreachable, so CI and
  offline runs stay green and no secret/network is required. Implemented as a `[Fact]` that probes the
  configured Ollama endpoint first and **early-returns with a logged reason** when it is unreachable
  — no new test dependency (chosen over `Xunit.SkippableFact` to avoid adding a package for a single
  test). The test is also `[Trait("Category", "RealAudio")]`-adjacent (tagged `LiveLlm`) so it is
  excluded from the default fast run.
- **Structure-only assertions:** a genuine non-empty answer card is produced with the required 4-part
  structure (definition → "I would" bullets → example → principle) and within the `MaxTokens` cap.
  No exact-text assertion (LLM output is nondeterministic).
- Claude is **not** automated (paid network call + API key); a manual check remains documented.

## Out of scope

- NAudio physical-device capture (no real hardware in CI; thin WASAPI glue).
- WPF overlay card visual rendering beyond the existing FlaUI smoke test.
- Claude live calls in automation.

## Security considerations

- No secrets touched. Ollama is local and keyless; the suite requires no API key. (Security rule:
  keys stay in Windows Credential Manager.)
- WAV fixtures contain only synthetic TTS interview questions — no real PII / transcripts.
- No new NuGet dependencies (the live test uses a plain `[Fact]` with an endpoint probe).
- The added LLM path is display-only (existing behaviour); no new privileged action is driven by
  model output.

## Testing strategy summary

| Layer | Coverage |
|---|---|
| Backend toggle | ViewModel load/save round-trip test |
| Real audio → Whisper → pipeline → card | 6 `RealAudio`-tagged scenarios (deterministic provider) |
| Real LLM answer path | 1 gated Ollama smoke (structure-only) |
| Per-speaker transcript surfaced | `CapturingTranscriptSink` assertions in every scenario |

## Open questions

None blocking. Confirmed decisions: capture-seam injection, pre-recorded **TTS** WAV fixtures,
**Medium** Whisper model, **Ollama** for the live test, backend toggle added to Settings.
