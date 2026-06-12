# Conversation Core — Design (Spec 1 of 3)

- **Date:** 2026-06-08
- **Status:** Approved (brainstorming) → ready for implementation plan
- **Scope:** Single conversation-state source of truth (issue A) + Me/Other routing model + per-turn cancellation (issue B)
- **Out of scope (separate specs):** endpointing-based segmentation (issue C, Spec 2), boundary-classifier reliability/observability (issue D, Spec 3)

## 1. Problem

`TranscriptPipelineService` holds one long-lived in-memory `Session`, but `GenerateAnswerHandler` runs in a **separate DI scope with its own `DbContext`** and advances a *different* `Session` copy. The two never reconcile, which causes:

1. **Stale `ActiveTurn`** — the pipeline's turn is stuck at `Detected` forever, so `Session.ActiveTurn` permanently points at the first turn.
2. **Status flip-flop / clobbering** — both contexts write the same turn rows (`repository.Update(session)` marks the whole graph `Modified`), so persisted `Status` and clarification IDs can be overwritten with stale values.
3. **Rule 8 (`AdditionalRequirement`) is dead in-process** — it keys on `PreliminaryReady`/`RefinedReady`, which the pipeline's copy never reaches.
4. **Single shared answer-cancellation token** — `_currentAnswerCts` cancels independent answers when a new question completes (a new question kills a prior in-flight answer).

These produced the user-visible "only one answer card, the rest are skipped" failures (PRs `c769753`, `6d20880`). Those PRs neutralised the symptoms deterministically; this spec removes the root cause.

## 2. Goals / Non-goals

**Goals**
- One authoritative live conversation state; routing decisions always see accurate turn status.
- Implement the Me/Other routing model (below) deterministically.
- Per-turn answer cancellation: distinct questions never cancel each other.
- Stop the persistence clobbering.

**Non-goals (explicitly deferred)**
- Speech segmentation / endpointing of multi-fragment questions (Spec 2).
- Boundary-classifier reliability, prompt, or observability beyond what already exists (Spec 3).
- Any UI/overlay change. Sinks (`IConversationTurnSink`, `IAnswerStreamSink`) keep their current contracts.

## 3. Conversation model (behaviour contract)

Speakers: `Me` = candidate (mic). `Other` = interviewer (system/loopback audio).

**Generation and regeneration are triggered exclusively by `Other`. `Me` only ever attaches context — it never opens a turn and never triggers generation.**

- **`Other` utterance** → existing semantic routing (heuristic → AI → safety net), now with *accurate* status:
  - new question → **new independent turn** (generated in parallel, no preemption of other turns);
  - continuation while collecting → append fragment;
  - clarification-response while `AwaitingClarification`/`ClarificationReceived` → regenerate that turn;
  - additional requirement on an answered turn (`PreliminaryReady`/`RefinedReady`) → regenerate that turn.
- **`Me` utterance** → **deterministic, no AI, never generates.** Record the utterance as clarification/context on the target turn (`CurrentTurn` = most recent non-terminal turn if one exists, else the most recent turn), then:
  - if the target turn is **not yet answered and not generating** (`Detected`/`CollectingQuestion`) → mark it `AwaitingClarification`; the next `Other` response regenerates the answer incorporating the recorded context;
  - if the target turn is **generating or already answered** (`GeneratingPreliminary/Refined`, `PreliminaryReady`/`RefinedReady`) → record context only — **no status change, no auto-regenerate** (this avoids racing the in-flight answer); the context is consumed by the next `Other`-triggered regeneration of that turn;
  - if there are **no turns yet** → hold/ignore.

This **supersedes PR #14's Me-path** (`6d20880`): `Me` no longer creates standalone cards, and the Me-path no longer depends on the AI classifier. The Other-path hardening (`c769753`) and the `BoundaryRoute` instrumentation are retained.

> The discriminator is **answered vs. not-yet-answered**, not merely terminal vs. non-terminal: an answered-but-undismissed turn (`PreliminaryReady`) is treated as the "last answered" question, so a `Me` utterance on it is a follow-up (context only), not a clarification-with-wait.
>
> Open decision carried forward: the follow-up (answered-turn) case defaults to **attach context, no auto-regenerate** (consistent with "only Other triggers generation"). Revisit if users want the manual-refine control to pick it up.

## 4. Architecture (Approach 1 — orchestrator owns state)

The pipeline keeps its single in-memory `Session` as the **authoritative live state**. The DB is **write-through** for history/crash-recovery and is never consulted for live routing. A **status-feedback channel** closes the loop from the background answer worker back to the pipeline.

```
 segment ─▶ [drain feedback ▸ apply status to in-memory turns] ─▶ classify ─▶ route ─▶ (maybe) fire generation
                      ▲                                                                          │
                      └──────────────── TurnStatusEvent ◀── GenerateAnswerHandler ◀─────────────┘
                                          (ITurnStatusFeedback)        (separate DI scope / background task)
```

**Why Approach 1 (and not the full single-store Approach 2):** Approach 1 is the lowest-risk change that removes the root cause — it leverages the pipeline's existing single-threaded consumer loop (no new locking for state) and fits the current DI-scope/EF reality. It is explicitly a **staged bridge** toward Approach 2 (one in-memory `ConversationStateManager` with DB write-behind), which is closer to the reference architecture and is recorded here as the future evolution (§9).

## 5. Components

### 5.1 `ITurnStatusFeedback` (new port — `Application/Abstractions/`)
Channel for the answer worker to report turn-lifecycle transitions back to the pipeline.
- Producer: `void Publish(TurnStatusEvent e)` — called by `GenerateAnswerHandler`.
- Consumer: `bool TryDrain(out TurnStatusEvent e)` (or an `IAsyncEnumerable`/`ChannelReader`) — drained by the pipeline at the top of each consumer iteration.
- `TurnStatusEvent` = `record(ConversationTurnId TurnId, ConversationTurnStatus Status)` (answer-version arrival is represented by the `PreliminaryReady`/`RefinedReady` transition).
- Implementation: singleton wrapping an unbounded `Channel<TurnStatusEvent>` (single reader, multi writer). Registered in DI as a singleton, like the existing sinks.

### 5.2 `GenerateAnswerHandler` (changed — `Application/Answers/Commands/`)
- Inject `ITurnStatusFeedback`; `Publish` on each transition it performs: `GeneratingPreliminary`/`GeneratingRefined`, then `PreliminaryReady`/`RefinedReady` (and on cancel/fail, publish the resulting status).
- **Remove `repository.Update(session)`** — rely on EF change-tracking so only the entities it actually modified (turn `Status`, new `AnswerVersion`, new `GeneratedAnswer`) are written. This is what stops clobbering the pipeline-owned columns (clarification IDs, pre-answer status).

### 5.3 `TranscriptPipelineService` (changed — `Application/Sessions/`)
- Inject `ITurnStatusFeedback`. At the top of `ProcessAsync`, **drain feedback and apply** each `TurnStatusEvent` via `turn.TransitionTo(status)` on the in-memory `session` (ignore events for unknown/terminal turns).
- **Me routing** (`HandleMeUtterance`): deterministic per §3; never reaches a turn-creating path. Add a speaker guard so `Me` segments bypass `HandleNewQuestion`/`HandleQuestionComplete`.
- **Other routing**: unchanged semantic path; retain PR #14 deterministic guards as defense-in-depth (now backed by accurate status, so Rule 8 fires).
- **Per-turn cancellation**: replace `_currentAnswerCts` with `ConcurrentDictionary<ConversationTurnId, CancellationTokenSource> _turnCts`.
  - `FireAndForget(command)` creates/registers the turn's CTS and passes its token.
  - Cancel a turn's CTS **only when regenerating that same turn** (clarification-received refine, additional-requirement, force-complete of that turn). New independent turns never cancel others.
  - Dispose a turn's CTS when it reaches a terminal status or is replaced.
- The pipeline writes only its owned changes via its own `IUnitOfWork`; it does not call `repository.Update`.

### 5.4 State queries (`Domain/Sessions/Session.cs`)
- `ActiveTurn` (existing) = most recent non-`Dismissed`/`Resolved` turn = `CurrentTurn`.
- Add `LastTurn` = most recent turn overall (for the all-terminal follow-up edge), if needed.

### 5.5 Status ownership (the partition)
- **Pipeline owns** pre-answer states: `Detected`, `CollectingQuestion`, `AwaitingClarification`, `ClarificationReceived`; plus transcript items, questions, turn creation, question text, clarification IDs.
- **Answer handler owns** `GeneratingPreliminary`/`GeneratingRefined`, `PreliminaryReady`/`RefinedReady`, answer versions.
- The two phases don't overlap; feedback syncs the in-memory copy, so both contexts converge on the same `Status` value (last-write-wins is harmless because the value matches).

## 6. Data flow (single segment)

1. Segment `(Speaker, text)` arrives on the pipeline's single-threaded consumer.
2. Drain `ITurnStatusFeedback`; apply transitions to in-memory turns.
3. Classify (Other path: heuristic → AI → safety net; Me path: skip classification, route deterministically).
4. Route per §3.
5. For a generation/regeneration command: register the turn's CTS, `FireAndForget` with its token.
6. Answer handler streams (existing sinks), publishes `TurnStatusEvent`s, persists status + answer versions (no full-graph update).
7. Next iteration: pipeline applies the feedback → in-memory status accurate.

## 7. Error handling

- AI failure on an `Other` segment → existing heuristic safety net. `Me` is deterministic (no AI dependency).
- `TurnStatusEvent` for an unknown/disposed/terminal turn → ignored.
- `OperationCanceledException` on a superseded same-turn regen → existing cancel handling (`answer.Cancel`).
- Concurrency: state is mutated only on the consumer thread (feedback drained there); the only cross-thread structure is the `ConcurrentDictionary` CTS registry (created/cancelled on consumer thread, token read on background thread).
- Crash mid-session: live in-memory state is lost (the session ends), but write-through persistence keeps completed turns/answers for history.

## 8. Prior art & alignment

This design follows established real-time conversational-agent practice; it also makes one deliberate, documented divergence.

| Decision | External practice | Alignment |
|---|---|---|
| Orchestrator owns turn state; AI is a classifier/generator tool, not the controller | "The orchestrator handles state transitions… rather than the LLM" (AssemblyAI); "Stop Letting the LLM Drive Your Voice Agent's State Machine" (Voxam) | ✅ |
| Single authoritative in-process state; AI fed structured state | "A unified approach runs … state storage … in a single process, treating the AI as a tool … keeping the system synchronized" (SignalWire) | ✅ |
| Per-response cancellation signal | "The cancellation stream indicates when the current substream should be cancelled" (VoiceStream); cancel in-flight generation explicitly (LiveKit) | ✅ (mechanism) |
| Streaming + async producer-consumer (max-latency overlap) | LiveKit / AssemblyAI pipeline articles | ✅ (already present) |
| Multi-stage detection: heuristic → LLM intent → confidence gate; recent-context window; dedup | Enterprise Sales Copilot (arXiv 2603.21416) | ✅ (already present) |
| Deterministic speaker roles from mic vs loopback | Most copilots infer roles heuristically | ✅ (we are ahead) |

**Deliberate divergence — no cross-question preemption.** Production voice agents are half-duplex with *barge-in = cancel the current response when the user speaks*. We are a **passive observer of a two-party conversation with no TTS**, so distinct `Other` questions run **in parallel, no preemption**; cancellation is reserved for **same-turn regeneration** only. Applying the standard "cancel-on-new-input" pattern here would re-introduce the original bug.

**Approach 1 is a bridge.** Reference "single source of truth" systems keep one in-process state and don't round-trip a DB mid-turn (≈ Approach 2). Approach 1 reaches the same goal at lower risk for our EF/DI reality; the two-writer partition (§5.5) is our own engineering and is intended to be retired by the Approach 2 evolution (§9).

Sources:
- AssemblyAI — Voice agent architecture / turn detection: https://www.assemblyai.com/blog/voice-agent-architecture , https://www.assemblyai.com/blog/turn-detection-endpointing-voice-agent
- LiveKit — Turn detection & sequential pipeline: https://livekit.com/blog/turn-detection-voice-agents-vad-endpointing-model-based-detection , https://livekit.com/blog/sequential-pipeline-architecture-voice-agents
- SignalWire — single-process orchestration: https://signalwire.com/blogs/product/voice-ai-agent-browser-control
- Voxam — Stop letting the LLM drive the state machine: https://voxam.hashnode.dev/stop-letting-llm-drive-voice-agent-state-machine
- VoiceStream — handling interruptions/cancellation: https://voice-stream.readthedocs.io/en/stable/cookbook/interruptions.html
- Enterprise Sales Copilot (arXiv): https://arxiv.org/pdf/2603.21416

## 9. Future evolution (out of this spec)

- **Approach 2:** replace the pipeline-owned `Session` + feedback channel with a single `ConversationStateManager` (in-memory source of truth, DB write-behind), retiring the two-writer partition.
- Revisit the no-active-question follow-up (auto-regenerate vs context-only) and a recency window for follow-up attachment.

## 10. Testing strategy

- **Unit (`TranscriptPipelineServiceTests`)**
  - Other: new question → new turn, and a second new question does **not** cancel the first (per-turn CTS).
  - Other: additional-requirement on a `PreliminaryReady` turn (status arrived via feedback) → regenerates that turn (Rule 8 alive).
  - Other: clarification-response while `AwaitingClarification` → regenerates.
  - Me: with an unanswered turn (`Detected`) → records clarification + `AwaitingClarification`, no generation, never creates a turn; the next Other response regenerates incorporating it.
  - Me: with an already-answered turn (`PreliminaryReady`) → records follow-up context only, no status change, no generation, no new turn.
  - Me: with no turns → hold (no turn, no command).
  - Cancellation: same-turn refine cancels that turn's prior generation; distinct turns are untouched.
  - Feedback: applying a `PreliminaryReady` event updates in-memory status and unlocks Rule 8 routing.
- **Integration**
  - Feedback channel handler→pipeline; two-context persistence partition leaves clarification IDs intact after a handler save.
- **E2E (from `develop`, system audio)**
  - Multiple `Other` questions → multiple parallel cards, each with an answer.
  - A `Me` clarification followed by an `Other` response → the card regenerates incorporating it.

## 11. Rollout

- Land on a `fix/`-or-`feature/` branch off `develop`; supersede PR #14's Me-path commit (`6d20880`) with the deterministic Me routing while keeping `c769753` and the `BoundaryRoute` instrumentation.
- Acceptance: all unit/integration tests green; E2E (system audio) shows parallel cards + clarification-driven regeneration.
