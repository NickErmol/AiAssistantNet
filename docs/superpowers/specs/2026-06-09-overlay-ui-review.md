# Spec 3c — Live-Assist Overlay UI Best-Practices Review

**Date:** 2026-06-09
**Scope:** UI/UX review of the interview-copilot answer-card overlay. Assess against
live-assist / streaming-AI UI best practices and produce a prioritized, low-risk gap list.
**Surfaces reviewed:** `MainOverlayWindow.xaml` (answer/turn panel), `ConversationTurnViewModel.cs`
(`TurnVm` / `AnswerVersionVm`), `Styles.xaml`, `DarkTheme.xaml` / `LightTheme.xaml`,
the streaming wiring in `App.xaml.cs` + `AnswerStreamSink`.

This is a **review** deliverable. The low-risk gaps marked **[implement now]** are addressed in
this same slice (App-layer only — XAML + VM presentation logic, no pipeline/Domain/Infra/EF change).
Everything marked **[defer]** is a separate brainstorming→plan cycle.

---

## How the answer card works today

A turn card (`ItemsControl` over `ConversationTurn.Turns`, newest inserted at top) renders:

1. **Question** — `❓ InitialQuestion`, yellow (`Brush.Semantic.Question`), semibold.
2. **Status line** — `StatusLabel` (e.g. "Generating…", "Refining…", "Ready", "Refined"),
   tiny + muted gray, the **same color for every state**.
3. **Answer** — `LatestVersion.Text` in a single `TextBlock`, **mono font** (`Cascadia Mono`),
   `TextWrapping=Wrap`, user-adjustable font size. Plain text — **no markdown rendering**.
4. **Action row** — Copy · Regen · Dismiss · Resolve · − · +.
5. **Follow-up row** — textbox + `→` (only when Follow-ups toggle is on).

Streaming: `OnChunk` appends to the latest `AnswerVersionVm.Text`, so tokens visibly grow in place.
Refinement (clarification / screen / manual regen): `CreateNewVersion` inserts a new
`AnswerVersionVm` at index 0, flips `IsLatest`, and points `TurnVm.LatestVersion` at it —
so the card's answer **swaps in place** and the prior version disappears from view.
The window is globally `Opacity=0.75` and `Topmost`, sized 600×640, resizable.

---

## Best-practice assessment

### 1. Latency / "thinking" feedback — **GAP [implement now]**
Best practice for streaming assistants: show an explicit *working* state from request→first token,
because the dead air before streaming starts reads as "frozen." Today the only signal between
`Detected` and the first token is the static muted text "Generating…" — no motion, no distinction
from a finished card. Once tokens flow there's implicit feedback (growing text), but the
pre-stream gap and the refine gap (`UpdatedContextReceived` / `GeneratingRefined`) look inert.
→ Add a visible busy indicator (animated) while the turn is in a generating state.

### 2. State glanceability / color coding — **GAP [implement now]**
For a glanceable stealth overlay, status should be readable in a fraction of a second. Right now
every status (`Generating`, `Ready`, `Refined`, `Awaiting clarification`, `Resolved`) is the same
muted gray text, so the user must *read* it. Best practice: encode state in color
(busy=accent, ready/refined=green, awaiting=question-yellow, terminal=muted).
→ Drive the status line color from the turn status.

### 3. Error surfacing — **GAP [implement now]**
`OnError` injects a version whose `Text` is `"[Error: …]"` rendered in the **same primary color**
as a normal answer — easy to misread as content. (Matches the known bug "API errors as raw JSON.")
Best practice: errors must be visually distinct (semantic error color) and never look like an answer.
→ Flag error versions and render them in a dedicated error color.

### 4. "Refining" vs "new card" affordance — **GAP [implement now]**
This is the central live-assist subtlety: a *new question* must look new, while a *refinement of an
existing answer* must signal "this changed" without yelling. Today a new Other question correctly
creates a new card at the top (good), but an in-place refinement silently replaces the visible text —
the prior version vanishes and there is **no cue the card just updated**, nor which *kind* of update
it was. The VM already carries `AnswerVersionVm.VersionLabel` ("Refined — after clarification",
"Screen analysis", "Follow-up", …) and `TimeLabel`, but **neither is shown**.
→ Show the latest version's label + time on the card so refinements are legible, and (optionally)
a brief highlight when the version changes.

### 5. Version history — **PARTIAL [defer]**
Each turn keeps an ordered `AnswerVersions` collection, but only `LatestVersion` is ever rendered;
there is no way to see or compare a superseded preliminary answer. Best practice for refine flows is
at least an accessible history (expander / "v2 of 3"). Deferring: needs a disclosure UI + design.

### 6. Answer formatting / the 4-part card pattern — **GAP [defer]**
The answer is a plain mono `TextBlock`. The intended answer-card structure
([[reference-answer-card-pattern]] — definition → cue + bullets → example → principle, with **bold**
sub-labels) renders its markdown as **literal `**` and `-` characters**, and mono is dense for prose.
This is the highest-value content gap but **not low-risk**: it needs a markdown/inline renderer (or
`RichTextBox`) and is coupled to the deferred `PromptBuilderService` change. Separate spec.

### 7. Streaming completion cue — **MINOR [defer]**
No distinct "done streaming" moment (e.g. caret while streaming that disappears on complete).
Subsumed by #1's busy indicator turning off; full streaming-caret polish deferred.

### 8. Accessibility — **MINOR [defer]**
`TurnCard` has an `AutomationId` but the question/status/answer aren't individually named for UIA,
and the busy state won't be announced. Action buttons expose their `Content` text (adequate).
Low-value for a deliberately stealthy overlay; note and defer.

### 9. Density / opacity — **ACCEPTABLE (by design)**
Global `Opacity=0.75` trades readability for stealth; newest-first ordering and the slim chrome are
good for glanceability. No change — but #1–#4 all *increase* glanceability within the existing density.

---

## Prioritized gap list

| # | Gap | Risk | Disposition |
|---|-----|------|-------------|
| 1 | Animated "thinking"/busy indicator during generating states | low (XAML + VM bool) | **implement now** |
| 2 | Color-coded status line by state | low (VM brush property) | **implement now** |
| 3 | Distinct error styling (not answer-colored) | low (VM flag + binding) | **implement now** |
| 4 | Refining-vs-new affordance: show version label + time, highlight on update | low (bind existing VM data) | **implement now** |
| 5 | Accessible version history / "vN of M" | medium | defer (needs disclosure UI) |
| 6 | Markdown render of the 4-part answer pattern | medium-high | defer (renderer + prompt change) |
| 7 | Explicit streaming-complete caret | low-med | defer (mostly covered by #1) |
| 8 | UIA names for question/status/answer + busy announce | low | defer (stealth overlay, low value) |

## Implemented in this slice (1–4)
App-layer only, no pipeline/Domain/Infra/EF changes, no new migration:
- `TurnVm` gains `IsBusy`, `StatusBrush`, and a `LatestVersionLabel` surface; `AnswerVersionVm`
  gains `IsError`/answer-brush.
- `MainOverlayWindow.xaml` answer card: busy indicator, colored status, version label/time line,
  error-colored answer.
- New semantic brushes (`Brush.Semantic.Error`, `Brush.Semantic.Busy`) added to both themes.

These follow the existing untested-presentation-logic convention (the App layer is covered only by
FlaUI UITests; `StatusLabel`'s switch is likewise untested). No new VM test project is introduced for
a few presentation bindings.
