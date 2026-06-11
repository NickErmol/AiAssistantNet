# Spec B — Answer-Depth Scaling + Configurable Token Cap

**Date:** 2026-06-11
**Scope:** Two related answer-generation improvements:
1. **Difficulty scaling** — the model matches answer depth to the question's difficulty (easy → 1–2
   sentences, hard → full structure), via a prompt instruction only.
2. **Configurable token cap** — the user sets the max answer length (tokens) in Settings, decoupled
   from difficulty. Raising it is how the user resolves truncation.

**Layer:** App + Application only. **No Domain change, no EF migration** — the cap lives at the
app-settings level (`AppSettingsDto`, settings.json), like `AnswerFontSize` / `OverlayOpacity` already do.

**Motivation:** A live run showed a hard answer truncated mid-word (`"…most DB limits are inef"`). Root
cause: the default `AnswerLength.ShortLength` maps to **300 tokens**, but a complete 4-part structured
answer needs ~500–700. Two fixes: let the user raise the cap, and stop padding easy questions.

---

## Background — current behavior

`PromptBuilderService.Build` (the audio/detected-question path) sets
`AnswerPrompt.MaxTokens = MapLengthToTokens(settings.Length)`:

| Length | Tokens | ≈ words |
|---|---|---|
| VeryShort | 150 | ~110 |
| ShortLength (default) | 300 | ~220 |
| Medium | 550 | ~410 |
| Detailed | 1000 | ~750 |
| DeepDive | 2000 | ~1500 |

`Length` currently does **double duty**: it controls both the **token cap** (`MapLengthToTokens`) and the
answer **structure** (`AppendStructureGuidance` — flat bullets vs grouped sub-labels vs worked example).
This spec splits those: **`Length` → structure only; the new cap → ceiling; difficulty → how much of the
ceiling the model actually uses.**

The audio `Build` has exactly one caller — `GenerateAnswerCommand` — which already loads
`AppSettingsDto` via `settingsStore.LoadAsync`. Screen (`BuildWithScreenMode`, `BuildScreenFollowUp`)
and follow-up (`BuildFollowUp`) paths are **out of scope** and unchanged (they already self-limit; screen
keeps its `Math.Max(MapLengthToTokens(...), 2000)` floor).

---

## 1. Configurable token cap (`MaxAnswerTokens`)

### Storage
Add to `AppSettingsDto` (Application/Sessions/Dtos):
```csharp
int MaxAnswerTokens = 800
```
Persisted in settings.json by `JsonSettingsStore`. **Not** in the Domain `AnswerSettings` owned entity →
no DB column, no migration. Read live at answer time, so changes take effect without a session restart.

### Defaults & range
- **Default: 800 tokens** (~600 words) — the smallest cap that lets a complete 4-part structured answer
  finish without truncation, below the screen path's 2000 floor.
- **Range: 200–4000**, slider step 50. Lower bound 200 = floor for "short but complete"; upper bound
  4000 (~3000 words) = practical ceiling for a live interview that also bounds cost/latency and keeps the
  standing security rule's "output stays capped" intact.

### Prompt builder change
`PromptBuilderService.Build` (both overloads — the `DetectedQuestion` overload delegating to the
`questionText` overload) gains an optional parameter:
```csharp
int? maxTokens = null
```
The returned prompt uses:
```csharp
MaxTokens: maxTokens ?? MapLengthToTokens(settings.Length)
```
Backward-compatible: callers/tests that omit it keep today's behavior. `MapLengthToTokens` stays (screen
+ follow-up paths still use it, and it is the fallback default).

### Handler change
`GenerateAnswerCommand` passes the loaded setting:
```csharp
var prompt = PromptBuilderService.Build(
    session.CodeProfile, session.AnswerSettings, questionText,
    /* …existing args… */,
    maxTokens: settings.MaxAnswerTokens);
```
(`settings` is already loaded earlier in the handler.)

### Validation
The settings-save path clamps `MaxAnswerTokens` into **[200, 4000]**. If a FluentValidation validator
exists for the settings command, add the rule there; otherwise clamp in the save handler. This preserves
the security rule that model output stays bounded.

### Legacy settings.json
Existing settings.json files predate `MaxAnswerTokens`. On load, a missing or non-positive value
(`<= 0`) coerces to the **800** default, so existing users are not broken. (System.Text.Json may supply
`0` for an absent positional-record parameter; the coercion guards against that regardless.)

---

## 2. Difficulty scaling (prompt-only, audio `Build` only)

Add one instruction to the audio `Build` **system** prompt. It acts as a tie-breaker permitted to
**override** the always-on (a)(b)(c) structure of the existing rule 2 for easy questions:

> *"Match depth to the question's difficulty: for a trivial or factual question, answer in 1–2 sentences
> and skip the bullet scaffold; for a complex design, trade-off, or implementation question, use the full
> structure. Never pad an easy question to fill space."*

- **No cap effect** — purely changes how much of the available ceiling the model uses.
- Placed so it reads as a refinement of rule 2 (which otherwise mandates the full scaffold every time).
- Screen and follow-up prompts are **not** touched.

---

## 3. Settings UI

`SettingsWindow.xaml` + `SettingsViewModel`:
- Add a `MaxAnswerTokens` property (`int`) to `SettingsViewModel`, loaded from / saved through the
  existing settings command flow (same pattern as the other answer settings).
- Add a labelled **slider** "Max answer length (tokens)" with a live numeric readout, `Minimum=200`,
  `Maximum=4000`, `TickFrequency`/step 50 — mirroring the existing opacity/font-size controls.
- Persisting goes through the same save path as the rest of the answer settings (no new command unless
  the existing one can't carry the field).

---

## Data flow

```
SettingsWindow slider → SettingsViewModel.MaxAnswerTokens → save → settings.json (AppSettingsDto)
                                                                          │
GenerateAnswerCommand: settingsStore.LoadAsync → settings.MaxAnswerTokens │
                                                                          ▼
            PromptBuilderService.Build(…, maxTokens: settings.MaxAnswerTokens)
                                                                          ▼
                       AnswerPrompt.MaxTokens = that value → Claude/Ollama
```

## Error handling
- Out-of-range cap → clamped to [200, 4000] before save.
- Missing/legacy value on load → 800.
- Omitted `maxTokens` arg → `MapLengthToTokens(Length)` fallback (unchanged behavior).

## Testing
**Deterministic (`AIHelperNET.Application.Tests`, platform-neutral):**
- `Build` with an explicit `maxTokens` ⇒ `AnswerPrompt.MaxTokens` equals it.
- `Build` with `maxTokens: null` ⇒ falls back to `MapLengthToTokens(settings.Length)` (regression guard
  for screen/follow-up parity and existing callers).
- The difficulty instruction text appears in the audio `Build` system prompt; does **not** appear in
  `BuildWithScreenMode` / `BuildFollowUp` (scope guard).
- `AppSettingsDto` default `MaxAnswerTokens == 800`.
- Legacy-load coercion: a settings payload with missing/`0` `MaxAnswerTokens` loads as `800`
  (test against `JsonSettingsStore` or the coercion helper).
- Validation/clamp: values below 200 and above 4000 are rejected/clamped.

**Manual (LLM-dependent, not CI-asserted):** on the live overlay, a trivial question (`"What's a primary
key?"`) yields 1–2 sentences; a hard question (`"How would you scale a service hitting DB limits?"`)
yields the full structured answer and **completes without truncation** at the 800 default. Same stance as
the boundary-classifier prompt wording — CI guards the deterministic wiring, not the model's prose.

## Out of scope
- Screen / follow-up prompt paths (cap and difficulty wording both audio-only).
- Any Domain `AnswerSettings` / EF change.
- A separate difficulty *classifier* call (the model self-assesses inline; no extra latency/cost).
- Auto-scaling the cap by difficulty (explicitly rejected — difficulty and cap are decoupled).
