# Answer-Card 4-Part Pattern + Markdown Rendering — Design

**Date:** 2026-06-10
**Branch:** `feature/answer-markdown-card`
**Scope:** Make the overlay answer card emit and render the intended 4-part answer
structure. Two coupled halves: (1) rewrite `PromptBuilderService.Build` to produce the
structured answer; (2) render that markdown in the overlay instead of printing literal
`**`/`-`. This is deferred UI-review gap #6 paired with the deferred prompt change.

This is an **App + Application** change only. No Domain, Infrastructure, or EF change, and
**no migration**.

---

## Problem

The generated answer is shown in a single mono `TextBlock`
(`MainOverlayWindow.xaml:418`, `Text="{Binding LatestVersion.Text}"`, `Cascadia Mono`).
Two gaps:

1. **Prompt** — `Build`'s rules cap output ("3–5 sentences or 3–4 bullets max", "plain prose
   or short bullets"), which fights the intended 4-part answer-card pattern
   (definition → cue + bullets → example → principle).
2. **Rendering** — even when the model emits markdown, the overlay prints literal `**` and
   `-`, and dense mono is poor for prose.

Target: the answer renders as a structured, glanceable card (see
`docs/superpowers/specs/2026-06-09-overlay-ui-review.md` gap #6 and the answer-card pattern
reference).

## Decisions (locked during brainstorming)

- **Renderer:** hand-rolled. A *pure* parser in Application + a thin WPF presenter in App.
  No new NuGet dependency (clean against the security rule); full theme control; tailored to
  the constrained subset the prompt emits.
- **Streaming:** render **on complete**. Stream raw mono text live (as today); parse + swap to
  the rendered card on `OnComplete`. Keeps parsing off the hot streaming loop; no half-parsed
  markdown on screen.
- **Prompt scope:** the explicit 4-part structure applies to **`Build` only**, scaled by
  `AnswerLength`. `BuildFollowUp` and `BuildWithScreenMode` keep their purpose-built shapes but
  get a shared "clean markdown" instruction so the renderer's input is consistent everywhere.

## Non-goals

- UI-review gaps #5 (version history), #7 (streaming caret), #8 (UIA names) — separate work.
- Full markdown (links, images, tables, blockquotes, italics, nested lists > 1 level).
- Changing the Copy behavior (still copies raw markdown `Text`).
- Live incremental markdown rendering during streaming.

---

## Components (7 units)

| # | Unit | Layer | Purpose |
|---|------|-------|---------|
| 1 | `MarkdownBlock` / `MarkdownInline` model | Application (`Answers/Markdown/`) | Pure record model. |
| 2 | `AnswerMarkdownParser.Parse(string)` | Application | Pure text → blocks. Unit-tested. |
| 3 | `MarkdownPresenter` control | App (WPF) | Maps blocks → themed visual tree. |
| 4 | `AnswerVersionVm` swap state + instance `OnComplete` | App | Drives streaming → rendered swap. |
| 5 | XAML presentation swap in the turn card | App | error / streaming / complete. |
| 6 | `PromptBuilderService.Build` rewrite + shared markdown rule | Application | The prompt half. |
| 7 | New theme brushes (code bg, inline-code, sub-label) | App (Dark/Light) | Theming. |

Layering: the parser (1+2) is pure and platform-neutral → Application, with real xUnit
coverage. The WPF presenter (3) is a dumb mapper with no parsing logic. App → Application is an
inward reference (NetArchTest-clean).

### 1–2. Markdown model + parser (Application, pure)

Model (records):

- `MarkdownBlock` (abstract) →
  - `ParagraphBlock(IReadOnlyList<MarkdownInline> Inlines)`
  - `ListBlock(IReadOnlyList<IReadOnlyList<MarkdownInline>> Items, bool Ordered)`
  - `CodeBlock(string? Language, string Code)`
- `MarkdownInline` (abstract) →
  - `TextRun(string Text)`
  - `BoldRun(string Text)`
  - `CodeRun(string Text)`

`AnswerMarkdownParser.Parse(string text) -> IReadOnlyList<MarkdownBlock>`:

**Supported subset (exactly what the prompt emits):**
- Block: paragraphs (blank-line separated), unordered bullets (`- ` / `* `), ordered lists
  (`1. `), fenced code blocks (```` ```lang … ``` ````).
- Inline: bold (`**text**`), inline code (`` `code` ``). A bold sub-label is just a paragraph
  whose leading inline is a `BoldRun`.

**Deliberately excluded:** headers (`#` — prompt bans them), links, images, tables,
blockquotes, italics, nested lists > 1 level.

**Defensive behavior (never throws):**
- Unclosed `**`/backtick → emit the marker run as literal text.
- Unterminated code fence → treat the remainder of input as the code block body.
- Unknown line prefix (`#`, `>`, …) → plain paragraph text.
- Empty/whitespace input → empty block list.

### 3. `MarkdownPresenter` (App, WPF)

A small `Control`/`UserControl` with dependency properties:
- `Markdown` (string) — on change, parse via `AnswerMarkdownParser` and rebuild content.
- `BaseFontSize` (double) — base size for prose; code uses the same base in the mono font.

Rendering rules:
- `ParagraphBlock` → a `TextBlock` (`TextWrapping=Wrap`) with `Inline`s: `TextRun`→`Run`,
  `BoldRun`→bold `Run` (sub-labels get `Brush.Markdown.SubLabel`), `CodeRun`→mono `Run` with
  `Brush.Markdown.InlineCode`.
- `ListBlock` → a vertical stack of rows; each row = bullet glyph (`•`) or `N.` + a wrapping
  `TextBlock` of the item inlines.
- `CodeBlock` → a `Border` (`Brush.Markdown.CodeBackground`) wrapping a mono `TextBlock`
  (`Cascadia Mono`, no wrap, horizontally scrollable).
- All colors via `DynamicResource` so theme switches apply live.

### 4. ViewModel swap state (`ConversationTurnViewModel.cs`)

- `AnswerVersionVm` gains:
  - `bool IsComplete` (observable). Streamed versions start `false`; set `true` on completion.
  - `string RenderedMarkdown => IsComplete && !IsError ? Text : string.Empty` — raises
    `PropertyChanged` only when `IsComplete` flips (NOT per chunk), so the parser runs **once**
    on completion.
- `OnComplete(turnId, versionType)` becomes an **instance** method: find the turn, find the
  latest version, set `IsComplete = true` (raises `RenderedMarkdown`). (Today it is a static
  no-op.)
- `CreateNewVersion` — streamed versions: `IsComplete=false`. `OnError` path:
  `IsComplete=true, IsError=true` (error versions do not stream).
- `OnChunk` unchanged (appends to `Text`).

### 5. XAML presentation swap (`MainOverlayWindow.xaml`, ~line 418)

Replace the single answer `TextBlock` with three stacked elements; Visibility via DataTriggers
(matching the existing trigger idiom in the file):

- **error** (`IsError == true`) → error-colored plain `TextBlock` *(unchanged behavior)*.
- **streaming** (`IsComplete == false && IsError == false`) → raw mono `TextBlock` bound to
  `Text` *(unchanged behavior)*.
- **complete** (`IsComplete == true && IsError == false`) → `MarkdownPresenter`
  `Markdown="{Binding LatestVersion.RenderedMarkdown}"`, `BaseFontSize` ← `AnswerFontSize`.

### 6. Prompt rewrite (`PromptBuilderService.Build`, Application)

New `Build` STRICT RULES:
1. Answer like an experienced engineer speaking — first person, spoken, no filler.
2. Structure: (a) 1–2 sentence definition/reframe to open; (b) first-person cue
   ("I would focus on:") + terse `-` bullets; (c) one-line closing principle.
3. Formatting: `**bold**` for emphasis/sub-labels, `-` for bullets. **No headers (`#`).**
   Code only in fenced ```` ```lang ```` blocks.
4. CODE only when the question asks to write/implement/fix/debug/show syntax *(unchanged)*.
5. Start directly with the answer — no "Great question", no restating the question.
6. Answer only — no "why this is a good answer" commentary.

Length scaling (reuses `MapLengthToTokens` tiers):
- **VeryShort / Short** → definition + 4–6 flat bullets + principle. No grouped sub-lists, no
  example.
- **Medium** → as above; bullets may group under `**sub-labels:**` where natural.
- **Detailed / DeepDive** → definition + grouped `**sub-label:**` sub-lists + a short concrete
  example + principle.

Shared "clean markdown" instruction added to `BuildFollowUp` and `BuildWithScreenMode` (no
4-part shape imposed): use `-` bullets, `**bold**`, fenced code, no headers.

### 7. Theme brushes (App, both Dark/Light)

- `Brush.Markdown.CodeBackground`
- `Brush.Markdown.InlineCode`
- `Brush.Markdown.SubLabel`

Bold uses the existing primary foreground, bolded.

---

## Testing

- **Parser** (`Application.Tests`, xUnit) — every block + inline type; mixed document; bold
  sub-label paragraph; ordered + unordered lists; fenced code with/without language; malformed
  (unclosed bold, unterminated fence); header-line passthrough; empty/whitespace.
- **PromptBuilderService** (existing test project) — `Build` system prompt contains the
  structure guidance; scales by `AnswerLength` (Short omits grouping/example language; DeepDive
  includes them); still bans headers; keeps code-only + answer-first rules; the shared markdown
  line is present in all three builders.
- **`MarkdownPresenter`** — pure presentation, verified by a **visual check** in a live answer
  (FlaUI cannot assert bold/inline runs), per the review's untested-presentation convention.
  Optional lightweight UITest: a completed card contains no literal `**`.

## Success criteria

- A completed conceptual answer renders the 4-part structure with real bold/bullets — no
  literal `**`/`-` on screen.
- Code answers render a mono code block; prose is proportional.
- Streaming still shows live raw text, then swaps cleanly to the rendered card on complete.
- Error versions remain visually distinct (error color, not answer-colored).
- Both Dark and Light themes render correctly.
- `dotnet build` clean (warnings-as-errors); existing suites green; **no migration**.

## Risks

- **WPF visual-tree rebuild correctness** — mitigated by keeping the presenter dumb and the
  parser fully unit-tested; the swap is gated on `IsComplete` so it runs once.
- **Prompt regressions** (model ignoring structure / over-formatting) — mitigated by length
  scaling + prompt tests; bounded by the existing `MaxTokens` cap.
- **Prompt-injection surface unchanged** — untrusted transcript/OCR is still fenced and
  labeled; output remains display-only (no privileged action on model output).
