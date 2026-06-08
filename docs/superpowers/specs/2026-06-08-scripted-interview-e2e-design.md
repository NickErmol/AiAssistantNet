# Scripted-Interview E2E (Tier A) — Design

- **Date:** 2026-06-08
- **Status:** Approved (brainstorming) → ready for implementation plan
- **Scope:** A deterministic, headless, automated end-to-end test that drives the **real** conversation pipeline with a scripted interview, replacing the unrunnable manual "system-audio E2E" gate for the Conversation Core work (PR #15).
- **Out of scope:** real audio capture, VAD, Whisper transcription, OCR, `SessionRunner` audio fan-out/merge, and AI-model accuracy (classifier/answer quality). Real-audio fidelity is a separate, later tier (B: fake capture + real Whisper; C: virtual-cable manual smoke).

## 1. Problem

CLAUDE.md requires an E2E pass before merging a feature because "unit tests don't catch pipeline wiring or runtime DB schema errors." For Conversation Core (PR #15) that gate is a **manual, live, system-audio interview** — it cannot be scripted, is non-deterministic, can't run in CI, and needs a human plus a microphone and loopback audio. We need an automated substitute that exercises the same wiring (real DI graph, real Mediator handler discovery, real EF/SQLite persistence, the real feedback channel and per-turn cancellation) deterministically.

> **DB schema note (verified against `develop`):** the project has **no EF migrations**; the app creates its schema with `EnsureCreatedAsync()` (`App.xaml.cs:36`), not `MigrateAsync()` — despite CLAUDE.md/memory claiming otherwise. This E2E uses `EnsureCreatedAsync()` to match production. It therefore covers DI-wiring + persistence round-trip + model→schema mapping, **not** migration drift (there are none). If the app adopts migrations later, switch the fixture to `MigrateAsync()` for drift coverage.

The conversation-core logic PR #15 changed lives entirely in `TranscriptPipelineService` (orchestration, `Me` routing, feedback drain, per-turn CTS) and `GenerateAnswerHandler` (status publishing, persistence partition, clarification-in-prompt). The two clean injection seams are the speaker-tagged ports `IAudioCaptureService` and `ITranscriptionService`; above them sits `TranscriptPipelineService.ProcessAsync(session, TranscriptItem, uow, ct)`, which consumes `(Speaker, text)` directly.

## 2. Goals / Non-goals

**Goals**
- Deterministically reproduce the two PR #15 acceptance scenarios (parallel answered cards; clarification-incorporated regeneration) in an automated test.
- Exercise the **real** DI registrations, Mediator handler, EF/SQLite persistence (`EnsureCreatedAsync`), `ITurnStatusFeedback`, and per-turn cancellation — catching wiring/persistence regressions.
- No network, no API cost, no audio hardware; runs in CI under `dotnet test` in seconds.

**Non-goals**
- Validating audio capture, Whisper accuracy, or `SessionRunner`'s fan-out/merge window (unchanged by PR #15; already covered by `SessionRunnerTests`).
- Validating classifier or answer-model quality — the two AI ports are faked with scripted behavior.

## 3. Architecture (Approach: direct pipeline driver, real DI host)

A headless xUnit fixture boots a real `ServiceProvider` from the production registrations `AddApplication()` + `AddInfrastructure()`, over a **shared open in-memory SQLite connection** with real `MigrateAsync()`. Only the two AI ports are overridden with fakes. The test drives the real `TranscriptPipelineService.ProcessAsync` with an **ordered** script of `(Speaker, text)` segments, awaiting each call, so `Me`/`Other` ordering is fully deterministic. The real `GenerateAnswerHandler` runs via real Mediator on background fire-and-forget tasks, advances turn status, publishes through the real `ITurnStatusFeedback`, and persists through real EF. Assertions read the real DB (via the repository) and a capturing answer sink.

```
 InterviewDriver.RunAsync(script)
   for each step (Speaker, text[, label]):
     (Other) enqueue label -> FakeQuestionBoundaryClassifier
     await pipeline.ProcessAsync(session, TranscriptItem(speaker,text,t), uow, ct)
        -> [real] drain feedback, classify (fake), route, FireAndForget
              -> Task.Run -> real Mediator -> real GenerateAnswerHandler
                   -> FakeAnswerProvider (echoes prompt) -> CapturingAnswerStreamSink
                   -> publish TurnStatusEvent (real ITurnStatusFeedback) -> persist (real EF)
     if step fired generation: await CapturingAnswerStreamSink.Completion(turnId, version)
   assert on DB + captured chunks
```

**Why this seam:** ordering of `Me` vs `Other` is the crux of the scenarios; driving `ProcessAsync` in sequence makes it exact. It directly targets the code PR #15 changed. `SessionRunner`'s audio plumbing is unchanged and already integration-tested, so skipping it loses no PR #15 coverage.

## 4. Components

All new code lives in `tests/AIHelperNET.Integration.Tests/E2E/`.

### 4.1 `InterviewHostFixture` (xUnit fixture, `IAsyncLifetime`)
- Builds a `ServiceCollection`, calls the real `AddApplication()` and `AddInfrastructure()`, then **replaces**:
  - `IAnswerProvider` / its resolver → `FakeAnswerProvider` (see 4.2),
  - `IQuestionBoundaryClassifier` → `FakeQuestionBoundaryClassifier` (see 4.3),
  - `ISettingsStore` → a stub returning deterministic settings (active backend, default `AnswerSettings`/`CodeProfile`) so no settings file/secret store is required,
  - `IAnswerStreamSink` → `CapturingAnswerStreamSink` (see 4.4).
- Uses a **single shared open** `SqliteConnection("Data Source=...;Mode=Memory;Cache=Shared")` kept alive for the fixture lifetime so every DI scope's `AppDbContext` sees the same schema/data; runs `await db.Database.EnsureCreatedAsync()` once at init (matching the app; the project has no migrations).
- Exposes the resolved `IMediator`, `TranscriptPipelineService`, `ISessionRepository`, `IUnitOfWork`, `ITurnStatusFeedback`, `FakeQuestionBoundaryClassifier`, and `CapturingAnswerStreamSink`.
- `DisposeAsync` disposes the provider and closes the SQLite connection.

> Audio/Whisper/OCR services remain registered by `AddInfrastructure()` but are **never resolved** by the test, so no hardware or models are touched (DI construction is lazy). If any such service is registered with eager/hosted-service side effects that break headless construction, the fixture removes that specific registration — to be confirmed during implementation by inspecting `AddInfrastructure()`.

### 4.2 `FakeAnswerProvider : IAnswerProvider`
- `Backend` returns the active backend used by the stub settings.
- `StreamAnswerAsync(prompt, ct)` yields a small canned answer that **embeds `prompt.User`** (e.g. `yield return "ANSWER>> " + prompt.User;`). Echoing the user prompt lets a test assert that specific context (the folded clarification text) reached the model — a concrete, deterministic proxy for "the answer incorporated the clarification."

### 4.3 `FakeQuestionBoundaryClassifier : IQuestionBoundaryClassifier`
- Holds a FIFO queue of `BoundaryClassificationResult` the test enqueues per `Other` step.
- `ClassifyAsync(...)` dequeues and returns the next scripted result with high confidence (≥ 0.9). If the queue is empty it returns `BoundaryClassificationResult.Ambiguous(latestItem.Text)`.
- `Me` items never reach the classifier (deterministic `Me` path), so no enqueue is needed for `Me` steps.

**Routing-determinism contract (important).** `BuildCommandWithBoundaryAsync` runs the **heuristic first** and only consults the classifier when the heuristic's confidence is `< 0.7`. So a scripted label routes the segment **only if the heuristic is ambiguous for that text**. The scenarios therefore choose `Other` texts deliberately:
  - Where the intended outcome is what the heuristic already produces at high confidence (a plainly-formed question → `NewQuestion`/`QuestionComplete`), the scripted label simply matches it and the classifier may not be consulted — fine.
  - Where the intended outcome is a label the heuristic would *not* confidently produce (notably `AdditionalRequirement` in Scenario 2), the text is chosen to be **heuristically ambiguous** (short, non-interrogative continuation phrasing) so the heuristic falls below 0.7 and the fake classifier's scripted label is what routes.

  The implementation pins each `Other` text against the real `QuestionBoundaryDetector` (using its existing unit tests + the `BoundaryRoute` log) so the intended route demonstrably fires; the asserts target the resulting turn/answer state, not the label itself.

### 4.4 `CapturingAnswerStreamSink : IAnswerStreamSink`
- `OnChunkAsync(turnId, version, chunk, ct)` appends to a per-`(turnId, version)` `StringBuilder`.
- `OnCompleteAsync(turnId, version, ct)` sets the `TaskCompletionSource` for that `(turnId, version)`, and `OnErrorAsync(...)` faults it.
- `Completion(turnId, version)` returns a `Task` the driver awaits; `Text(turnId, version)` returns the accumulated answer text for assertions.
- Completion keys are created on demand so the driver can await a key registered the moment generation starts.

### 4.5 `InterviewDriver`
- `RunAsync(Session session, IReadOnlyList<Step> steps)` where `Step = (Speaker Speaker, string Text, BoundaryLabel? Label, bool ExpectGeneration)`.
- For each step: if `Other`, enqueue `Label` into the fake classifier; `await pipeline.ProcessAsync(session, TranscriptItem.Create(Speaker, Text, t, 0.95f), uow, ct)` with a monotonically increasing timestamp `t`; if `ExpectGeneration`, `await` the sink completion for the turn the step targeted (resolved from `session.ConversationTurns` — the active/last turn after the call) with a bounded timeout.
- Timestamps strictly increase across steps so `Me` clarification items sort after the turn's `CreatedAt` (needed for clarification-in-prompt folding).

## 5. Quiescence / determinism (the crux)

Generation is fire-and-forget on a background task; the pipeline's in-memory `Session` only reflects `PreliminaryReady`/`RefinedReady` when a subsequent `ProcessAsync` drains `ITurnStatusFeedback`. The determinism rests on the handler's **statement order**: `GenerateAnswerHandler` calls `feedback.Publish(readyStatus)` **before** `streamSink.OnCompleteAsync(...)`. Therefore, once the driver **awaits the `CapturingAnswerStreamSink` completion signal** for a step, the ready `TurnStatusEvent` is already enqueued in the (unbounded, order-preserving) feedback `Channel`, so the **next** `ProcessAsync` call's `DrainStatusFeedback` is guaranteed to apply it before routing. No `Task.Delay`, no polling. A bounded timeout on each completion await turns a stuck pipeline into a clear test failure naming the `(turnId, version)`.

> This publish-before-complete ordering is a load-bearing assumption the E2E depends on; if a future change reorders those two calls in the handler, Scenario 2 would become racy. The plan notes this so the assumption is documented at both ends.

## 6. Scenarios (acceptance)

**Scenario 1 — parallel answered cards, no cross-cancel.**
1. `Other "What is dependency injection?"`, label `QuestionComplete`/`NewQuestion`, expect generation → await.
2. `Other "Now explain CQRS."`, label `NewQuestion`, expect generation → await.

Assert (reload session from the repo): exactly **2** conversation turns; **each** has ≥1 persisted `AnswerVersion`; both captured answer texts are non-empty; the first turn's answer is intact (the second question did not cancel it — guaranteed by the per-turn CTS, and observable as both completions firing without an `OnError`/cancel).

**Scenario 2 — clarification incorporated into regeneration.**
1. `Other "What is dependency injection?"`, label `NewQuestion`, expect generation → await `PreliminaryReady`.
2. `Me "do you mean constructor injection specifically?"` (no label; deterministic `Me` path) → attaches as clarification context on the now-`PreliminaryReady` turn; no generation.
3. `Other "yes, constructor injection specifically"`, label `AdditionalRequirement` (Rule 8 on a `PreliminaryReady` turn, unlocked by the drained feedback) → expect regeneration → await.

Assert: the turn now has **≥2** answer versions; the **latest** captured answer text **contains "constructor injection"** (echoed from the folded clarification transcript item) — jointly proving feedback drain (Rule 8 alive in-process), `Me` context-only routing on an answered turn, Rule 8 regeneration, and clarification-in-prompt folding.

> The `AwaitingClarification` path (`Me` on an *unanswered* turn → next `Other` response refines) is already covered deterministically by unit tests in `TranscriptPipelineServiceTests`; Scenario 2 uses the answered-turn + `AdditionalRequirement` path because it is race-free in an end-to-end setting.

## 7. Error handling

- Each completion await is bounded (e.g. 10 s); on timeout the test fails with the `(turnId, version)` that never completed.
- A `FakeAnswerProvider` or handler exception surfaces via `CapturingAnswerStreamSink.OnErrorAsync`, which faults the completion task → the awaiting driver step fails with the message.
- The fixture fails fast if `EnsureCreatedAsync` throws (a schema/model misconfiguration surfacing at startup).

## 8. Testing strategy

These tests *are* the verification artifact; they run with `dotnet test` (project `AIHelperNET.Integration.Tests`). No unit tests are written for the fakes themselves beyond what the scenarios exercise (YAGNI), except a tiny sanity test that `CapturingAnswerStreamSink.Completion` completes on `OnCompleteAsync` and faults on `OnErrorAsync` (it is the determinism linchpin).

## 9. Rollout

- Land on a branch off `develop` (independent of PR #15/#16; can merge in any order — touches only the test project).
- Once merged, the manual system-audio gate for conversation-core regressions is replaced by this automated E2E; the manual virtual-cable smoke (Tier C) remains an optional pre-release check.

## 10. Open questions (deferred, non-blocking)
- Whether any `AddInfrastructure()` registration has eager side effects requiring removal in the fixture — resolved by inspection during implementation.
- Shared-cache vs single-open-connection SQLite for the in-memory DB across scopes — pick whichever the existing `AppDbContext` options support cleanly; both keep one schema alive for the fixture.
