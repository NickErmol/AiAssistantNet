# Configurable Topic-Adaptive Transcription Glossary — Design

**Date:** 2026-06-16
**Branch:** `feature/configurable-transcription-glossary`
**Status:** Approved design → ready for implementation plan

## Problem

Analysis of the 2026-06-15 live interview session showed ~88% of interviewer questions were
answered correctly, but the hard misses traced to **Whisper ASR garbling domain vocabulary**:
"N+1 query" → *"end-less one problem"*, "Azure Key Vault" → *"EWALT"*, "Transient" → *"Trench"*.
These garbles fed the classifier/answer pipeline the wrong topic, producing wrong or clarifier-only cards.

`WhisperTranscriptionService` already biases decoding with an `InitialPrompt`
(`src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs:18`), but a latent bug
drops it: once any segment is emitted, `lastEmitted` *replaces* the prompt
(`WithPrompt(lastEmitted ?? InitialPrompt)`, line 49), so domain biasing is lost for the rest of the
session — exactly when the hard technical terms appear.

## Goal

Bias Whisper transcription toward the candidate's actual tech stack using a large, **configurable**,
**topic-adaptive** glossary covering 9 domains, so domain terms transcribe correctly. Keep biasing
active for the whole session (fix the overwrite bug).

## Non-goals

- Runtime network fetching of the glossary. Term lists are **web-researched at dev time and baked
  into the repo**. The app is local, stealth, and secrets-sensitive — a runtime network call for
  vocabulary adds a dependency and privacy surface for no benefit.
- Changing the boundary classifier / answer pipeline (separate, larger work tracked in
  `project-session-accuracy-analysis`).

## Decisions (user-approved)

| Decision | Choice |
|---|---|
| Term selection under Whisper's ~224-token prompt cap | **Topic-adaptive** — store the big glossary, inject only terms relevant to the recent transcript |
| Configurability | **Per-domain toggles** in Settings |
| Sourcing | **Web research + curate** at dev time, baked in |
| `cloude` meaning | General **cloud / distributed-systems** terms |
| Default enabled domains | All 9 (topic-adaptive keeps the injected subset small anyway) |

## Domains (9)

`dotnet`, `angular`, `react`, `sql`, `mssql`, `postgres`, `azure`, `aws`, `cloud`.

SQL / MSSQL / Postgres are separate toggles because their dialect vocabulary differs
(generic SQL vs T-SQL vs PL/pgSQL).

## Architecture

Clean/Hexagonal, dependencies point inward. New pieces:

### 1. Glossary data (Infrastructure resource)
- File: `src/AIHelperNET.Infrastructure/Transcription/Glossary/glossary.json`, embedded resource.
- Shape per domain: `{ "key": "...", "displayName": "...", "core": [3-6 terms], "terms": [20-60 terms] }`.
  - `core` — always injected when the domain is enabled (high-value, frequently-spoken terms).
  - `terms` — the adaptive candidate pool.
- Optional override: if `<dataRoot>\glossary.json` exists, load it instead of the embedded default,
  so the user can extend without rebuilding. (`AppPaths` provides the data root.)

### 2. Domain models (Application)
- `GlossaryDomain` record: `Key`, `DisplayName`, `Core` (IReadOnlyList<string>), `Terms` (IReadOnlyList<string>).
- Lives in Application so both the pure selector and its tests can use it without Infrastructure.

### 3. Selection algorithm (Application, pure + unit-testable)
- `GlossarySelector.Select(IReadOnlyList<GlossaryDomain> domains, IReadOnlySet<string> enabledKeys,
  string recentContext, int wordBudget)` → ordered `IReadOnlyList<string>` of terms.
- Algorithm:
  1. Include each enabled domain's `core` terms (these are the floor).
  2. Score every remaining enabled term by lexical overlap with `recentContext`
     (case-insensitive word/stem overlap; small boost for whole-term substring presence).
  3. Greedily add highest-scored terms until `wordBudget` (default ~110 words) is reached.
  4. If `recentContext` is empty (session start), fall back to cores + round-robin first terms per domain.
- Deterministic given inputs. No IO. No randomness.
- Helper `GlossaryPromptBuilder` (or a method on the provider) joins selected terms into the prompt
  suffix string.

### 4. Port + provider
- `Application/Abstractions/ITranscriptionGlossaryProvider`:
  - `string BuildPrompt(IReadOnlySet<string> enabledKeys, string recentContext, int wordBudget)`.
  - `IReadOnlyList<GlossaryDomain> Domains { get; }` (for the Settings UI to list toggles).
- `Infrastructure/Transcription/JsonTranscriptionGlossaryProvider` implements it: loads JSON
  (override → embedded default), holds parsed `GlossaryDomain`s, delegates selection to `GlossarySelector`.
- Registered in DI as a singleton (data is immutable after load).

### 5. Settings
- `AppSettingsDto` gains:
  - `bool GlossaryEnabled` (default `true`).
  - `string[] EnabledGlossaryDomains` (default = all 9 keys).
- `Normalized()` lowercases, dedupes, and drops unknown keys (validate against provider domain keys
  where available, else pass through and let the provider ignore unknowns).
- String keys → the enum-as-number serialization gotcha does not apply.

### 6. Whisper wiring
- Inject `ITranscriptionGlossaryProvider` + the means to read enabled domains/`GlossaryEnabled`
  (thread through `TranscribeAsync` the same way `model`/`language` are threaded from settings, OR
  inject `ISettingsStore` — implementer verifies the existing call site in the pipeline).
- Maintain a small ring buffer (last ~6 emitted segments) inside the service as `recentContext`.
- Build the prompt as: `base interview preamble` + `glossary suffix` + `rolling lastEmitted`,
  combining them instead of letting `lastEmitted` overwrite the bias. Keep total under the cap
  (budget the glossary at ~110 words, preamble + lastEmitted fit in the remainder).
- When `GlossaryEnabled` is false, behavior reverts to today's `InitialPrompt`/`lastEmitted` logic.
- Applies to subsequent transcription windows live; no restart needed.

### 7. Settings UI
- Settings window (Transcription/Appearance area): master toggle
  "Bias transcription with tech glossary" + 9 per-domain checkboxes.
- Bound to `SettingsViewModel`; checkbox state ↔ `EnabledGlossaryDomains`; master ↔ `GlossaryEnabled`.
- Follow the existing Settings styling/UIA-naming conventions.

## Data flow

```
settings (GlossaryEnabled, EnabledGlossaryDomains)
        │
        ▼
WhisperTranscriptionService (ring buffer of recent segments = recentContext)
        │  BuildPrompt(enabledKeys, recentContext, budget)
        ▼
ITranscriptionGlossaryProvider ── GlossarySelector.Select(...) ──► term list
        │
        ▼
prompt = preamble + glossary suffix + recent context  ──► Whisper WithPrompt(...)
```

## Error handling
- Malformed/missing `glossary.json` override → log a warning, fall back to embedded default;
  never crash transcription.
- Empty enabled set or `GlossaryEnabled=false` → no glossary suffix (today's behavior).
- Selection never exceeds the word budget (hard cap before returning).

## Testing
- **Unit (`GlossarySelector`)**: budget respected; cores always present when domain enabled;
  context-matching terms rank above unrelated ones; empty-context fallback; disabled domains excluded.
- **Unit (settings)**: `Normalized()` lowercases/dedupes/drops unknown domain keys.
- **Unit (glossary JSON)**: embedded resource parses; no duplicate domain keys; each domain has
  non-empty `core` and `terms`.
- **Tier-C regression (opt-in, real Whisper, CPU-pinned)**: a clip containing "N+1 query" and
  "Azure Key Vault" transcribes those terms verbatim — proves the two real ASR misses are fixed.
  (Gated like existing Tier-C tests; not run in normal CI.)
- Full `dotnet build` (TreatWarningsAsErrors) + `dotnet test` green.

## Execution plan (subagent-driven, one worktree)

Worktree: `D:\work\AIHelperNET-glossary`, branch `feature/configurable-transcription-glossary`.
Implementer subagents use **claude-sonnet-4-6** (per standing preference).

- **A** (independent, first/parallel): web-research + author `glossary.json` for the 9 domains
  to the agreed schema.
- **B** (parallel with A): `GlossaryDomain` model + `GlossarySelector` + TDD unit tests
  (needs only the schema, not the real data).
- **C** (after B): `AppSettingsDto` fields + `Normalized()` + port + `JsonTranscriptionGlossaryProvider`
  + DI registration + Whisper wiring + the `lastEmitted` overwrite fix + glossary-JSON test.
- **D** (after C): Settings UI per-domain toggles + `SettingsViewModel` binding.
- **E**: Tier-C regression test + full build/test verification, then PR `feature/...` → `develop`.

Sequencing: A ∥ B → C → D → E. Steps share `GlossaryDomain`/schema, so B fixes the schema that A and C consume.

## Risks / notes
- Whisper prompt biasing only *nudges* greedy decoding (`WithTemperature(0)`); it strongly helps
  near-miss homophones (Key Vault/EWALT) but is not a guarantee — the Tier-C test is the real proof.
- Threading enabled-domains into `TranscribeAsync` depends on the existing call site; implementer
  verifies rather than assumes.
- Keep the App project unlocked: stop any running overlay before building (MSB3027 file-lock gotcha).

## Manual verification (Tier-C, opt-in)

This procedure exercises the full live path: ASR → glossary-biased prompt → transcript display.
Requires a working Whisper model download and a loopback-capable audio device.

**Setup**

1. Launch the overlay using the `run-aihelper` skill (stops any existing instance, rebuilds Debug, runs).
2. Open **Settings → Transcription** (or equivalent).
3. Enable the **Glossary master toggle**.
4. Tick the **`.NET / C#`** and **`Azure`** domain checkboxes. Save.

**Play test phrases via TTS → loopback**

Use Windows `System.Speech.Synthesis.SpeechSynthesizer` (or `Add-Type` in PowerShell) to speak the
phrases through the default render device. The `WasapiLoopbackCapture` sink tags the audio as
`Speaker.Other`, so the app treats it as an interviewer question (see the
`reference-drive-app-via-tts-loopback` memory note for the technique).

Phrases to speak:
- `"Can you explain the N plus one query problem with Entity Framework Core?"`
- `"How would you store secrets in Azure Key Vault using managed identity?"`

**Expected results**

- The **transcript panel** should display:
  - `"N plus one query"` and `"Entity Framework Core"` intact (not garbled as *"endless one"* /
    *"Trench"*).
  - `"Azure Key Vault"` and `"managed identity"` intact (not garbled as *"EWALT"*).
- Answer cards for each question should appear (glossary affects ASR only; answer quality validates
  the whole pipeline).

**Pass criterion:** both domain-specific terms appear verbatim in the live transcript. Any garble
that was present before the glossary fix reappears when the toggle is disabled — confirming the
glossary suffix is the causal factor.
