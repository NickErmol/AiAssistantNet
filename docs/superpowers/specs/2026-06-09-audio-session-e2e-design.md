# Tier B — `SessionRunner` Audio-Session Orchestration E2E

**Date:** 2026-06-09
**Status:** Design approved → implementation plan next.
**Scope:** Add deterministic, CI-runnable automation tests that exercise the audio-session
orchestration loop (`SessionRunner`) from `AudioFrame` ingestion through transcription, segment
merging, and into the real answer pipeline. This is the "Tier B" the scripted-interview E2E
deferred — it covers the layer *above* `TranscriptPipelineService.ProcessAsync` that Tier A skips.

## Motivation

`src/AIHelperNET.App/Services/SessionRunner.cs` holds substantial, **currently untested**
orchestration:

- per-speaker frame fan-out (`frame.Speaker` → mic vs loopback channel), gated by `AudioSourceMode`
  (`runMic` / `runLoopback`);
- two parallel VAD+Whisper transcription tasks feeding one merge channel;
- a sequential merge consumer with a 300 ms `SegmentMergeWindowMs` that greedily merges consecutive
  same-speaker segments into one transcript item, flushes on speaker change, and drains +
  `FlushAccumulatorAsync` on completion;
- `pipeline.ProcessAsync` is called sequentially (the `Session` aggregate is not thread-safe).

Tier A (`ScriptedInterviewE2ETests`) starts *below* this, calling `ProcessAsync` directly, so none
of the fan-out / merge / flush / drain logic is covered by any automated test. The individual leaf
components (`SegmentAccumulator`, `SileroVadDetector`, `TranscriptPipelineService`) are unit-tested;
the **wiring that turns an audio session into turns is not**.

## Non-goals (deferred)

- **B2 — real-Whisper fidelity:** running actual `Whisper.NET` against a bundled WAV. Needs a model
  file + audio fixtures + env-gating (cannot run in CI); a separate opt-in deliverable, like the
  Spec 3b env-gated Haiku eval.
- **FlaUI-through-audio:** driving the WPF overlay via a fed audio session. Compounds the flakiest
  layer for the least determinism — rejected.
- **`VadWindowAccumulator` unit tests** — a worthwhile but separate gap.

## Architecture — reuse the Tier A harness

New code lives in `tests/AIHelperNET.Integration.Tests/E2E/`. The Integration.Tests project already
references the App project, so `SessionRunner` is directly constructable.

- Reuse `InterviewHost` unchanged: real `AddApplication()` + `AddInfrastructure()` over an in-memory
  shared-cache SQLite (migrated), with the AI ports, settings store, and sinks faked, plus the
  `CapturingAnswerStreamSink` and `FakeQuestionBoundaryClassifier` already exposed by the host.
- The test constructs a **real** `SessionRunner` directly:
  ```csharp
  var runner = new SessionRunner(
      host.Services.GetRequiredService<IServiceScopeFactory>(),
      fakeCapture,            // ScriptedAudioCaptureService
      fakeTranscription,      // ScriptedTranscriptionService
      host.Services.GetRequiredService<TranscriptPipelineService>(),
      segmentMergeWindowMs: 120);
  ```
  The DI-registered NAudio / Whisper services are never resolved — the fakes are passed positionally.
  `SessionRunner` resolves `ISessionRepository` / `IUnitOfWork` from the host scope factory (real),
  so the session must be persisted before `StartAsync` (as the Tier A tests already do).

## Components

### New test doubles (in `E2E/`)

**`ScriptedUtterance`** — `record ScriptedUtterance(Speaker Speaker, string Text, int GapMsBefore);`
An ordered script of utterances drives both fakes.

**`ScriptedAudioCaptureService : IAudioCaptureService`**
- Constructed with `IReadOnlyList<ScriptedUtterance>`.
- `CaptureAsync(selection, ct)` yields one `AudioFrame` per utterance, in order:
  `await Task.Delay(GapMsBefore, ct)` then
  `yield return new AudioFrame(new[] { (float)index }, utterance.Speaker, capturedAt)`.
  The utterance index is encoded in `Samples[0]` for explicit, order-independent correlation.
  `capturedAt` advances by a fixed step so segments have monotonic timestamps.
- Honors `ct` (stops promptly on `StopAsync`); completes the stream after the last utterance so the
  whole `SessionRunner` loop terminates on its own.

**`ScriptedTranscriptionService : ITranscriptionService`**
- Constructed with the same `IReadOnlyList<ScriptedUtterance>`.
- `TranscribeAsync(frames, model, language, ct)` reads each frame and emits exactly one
  `TranscriptSegment(script[(int)frame.Samples[0]].Text, frame.Speaker, frame.CapturedAt, 0.95f)`.
- Invoked concurrently for the mic and loopback channels; correlation is by the per-frame index, so
  no shared mutable state is required (read-only script) — inherently thread-safe.

### Production change (one, minimal)

`SessionRunner` gains a constructor parameter:
`public sealed class SessionRunner(..., int segmentMergeWindowMs = 300)`, and the body uses the field
instead of the `const int SegmentMergeWindowMs = 300`. The DI registration in
`App/DependencyInjection.cs` uses the default (no behavior change in production). Tests pass a small
value so timing-sensitive cases run fast and robustly.

## Test scenarios — `SessionRunnerAudioE2ETests` (`IAsyncLifetime`, one `InterviewHost` per test)

For each: create + persist a `Session`, script the `FakeQuestionBoundaryClassifier` like Tier A
(choosing Other texts that the real heuristic routes deterministically), `StartAsync`, await answer
completion via the sink + a DB poll, `StopAsync`, then assert on the reloaded `Session` and
`Sink.Errors`.

1. **Happy path** — one Other utterance → one answered turn (`ConversationTurns` has 1 turn,
   ≥1 answer version; `Sink.Errors` empty). Proves capture→transcription→merge→pipeline end to end.
2. **Same-speaker merge** — two Other fragments with `GapMsBefore = 0` → merged into a single
   transcript item → exactly **one** turn. The turn count is the discriminating signal: without the
   merge the two fragments would be classified separately (two scripted `NewQuestion` results → two
   turns), so asserting a single turn locks the greedy same-speaker merge inside the window. (The
   turn's question text comes from the scripted classifier's normalized text, not the raw merge, so
   it is not asserted.)
3. **Speaker-change flush** — Other, then Me → the Other segment flushes immediately on the speaker
   change; the Me utterance follows the deterministic clarification path (no new turn, attaches
   context). Exercises fan-out, both transcription channels, and speaker-change flush.
4. **Audio-source gating** — `audioSource = SystemAudioOnly` with a Me utterance present in the
   script → Me frames are dropped (`runMic = false`); only the Other utterance produces a turn.

## Determinism & timing

- The only real waits are `Task.Delay`s driven by `GapMsBefore`. The merge scenario uses `GapMsBefore
  = 0` for the second fragment, so both segments reach the merge channel within milliseconds —
  comfortably inside any window, robust under CI load. The flush scenario uses `GapMsBefore ≈
  segmentMergeWindowMs × 2` so the window expires first. With `segmentMergeWindowMs = 120`, the
  longest deliberate wait is ~240 ms per timing-sensitive test.
- Completion is awaited via the capturing sink plus a bounded DB poll (mirroring `InterviewDriver`),
  never a fixed sleep. `StopAsync` then joins the background loop.
- Known constraint inherited from Tier A: the `FakeQuestionBoundaryClassifier` FIFO can hold a stale
  scripted result when the heuristic bypasses it (confidence ≥ 0.7). Scenarios pick Other text the
  heuristic routes predictably, or enqueue scripted results accordingly — same discipline as Tier A.

## Error handling

- Fakes honor `ct` and complete their streams so the loop cannot hang; tests wrap waits in a bounded
  timeout. A scenario asserts `Sink.Errors` is empty (no pipeline exceptions).
- `StopAsync` already guards a 5 s drain timeout; tests rely on it for teardown.

## Files

- New: `E2E/ScriptedAudioCaptureService.cs`, `E2E/ScriptedTranscriptionService.cs`,
  `E2E/ScriptedUtterance.cs` (or folded into one small file), `E2E/SessionRunnerAudioE2ETests.cs`.
- Changed (prod): `src/AIHelperNET.App/Services/SessionRunner.cs` (merge-window constructor param).
- No migration, no Domain/Application/Infrastructure change.

## Verification

- `dotnet test tests/AIHelperNET.Integration.Tests` green (new scenarios pass; the pre-existing
  SQLite-lock flakiness on `Scenario1` is unrelated and out of scope).
- `dotnet build` clean (0 warnings under `TreatWarningsAsErrors`).
