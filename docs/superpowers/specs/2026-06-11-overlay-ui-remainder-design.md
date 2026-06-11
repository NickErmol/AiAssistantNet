# Spec A — Overlay UI Remainder (§5 version pager, §7 streaming caret, §8 UIA names)

**Date:** 2026-06-11
**Scope:** Implement the three items deferred from the
[2026-06-09 overlay-UI best-practices review](./2026-06-09-overlay-ui-review.md):
§5 version history, §7 streaming-completion cue, §8 accessibility.
**Layer:** App only — XAML + ViewModel presentation logic. **No** pipeline / Domain /
Infrastructure / EF change, **no** migration. Same constraint items 1–4 of the review honored.

Items 1–4 (busy indicator, color-coded status, error styling, refining-vs-new affordance) are
already shipped. This slice closes out the deferred remainder.

---

## §5 — Version pager (the substantive piece)

### Problem
Each turn keeps an ordered `AnswerVersions` collection (newest at index 0), but the card only ever
renders `LatestVersion`. A superseded preliminary or screen-analysis answer is unreachable — there is
no way to look back at what an earlier version said.

### Design — a *displayed* version distinct from *latest*
`TurnVm` today exposes `LatestVersion` (always index 0) and binds the whole answer area to it. We
introduce a **displayed** version that the UI binds to, decoupled from which version is newest:

- **`DisplayedVersion`** (`AnswerVersionVm?`, observable) — the version currently shown. The card's
  version-label line and answer Grid (streaming / markdown / error) bind to this instead of
  `LatestVersion`.
- Derived, change-notified properties:
  - `HasMultipleVersions` ⇒ `AnswerVersions.Count > 1` (drives pager visibility).
  - `VersionPositionLabel` ⇒ `"v{n} / {m}"` in **chronological** order, so `v1` = oldest and
    `v{m}` = newest. With a newest-first list: `n = Count - IndexOf(DisplayedVersion)`, `m = Count`.
  - `CanShowOlder` ⇒ a strictly older version exists (displayed is not the last/oldest).
  - `CanShowNewer` ⇒ a strictly newer version exists (displayed is not index 0).

### Snap-to-newest behavior (preserve today's UX)
`CreateNewVersion` already inserts at index 0, flips `IsLatest`, and points `LatestVersion` at the new
version. It additionally sets `DisplayedVersion = newVersion` and raises the four derived properties.
**Net effect unchanged from today:** a fresh preliminary / refine / screen / follow-up still jumps the
card to the newest answer. Navigation only matters *after* completion, when the user steps back.

### Navigation commands
Two `[RelayCommand]`s on `ConversationTurnViewModel`, matching the existing `Regenerate` /
`Dismiss` / `Resolve` pattern that takes a `TurnVm?`:

- `ShowOlderVersion(TurnVm?)` — move `DisplayedVersion` one step toward older (index +1), clamped.
- `ShowNewerVersion(TurnVm?)` — move one step toward newer (index −1), clamped.

Each re-raises the derived pager properties on the turn. Button `IsEnabled` binds to
`CanShowOlder` / `CanShowNewer`, so commands are effectively no-ops at the bounds (defensive clamp
regardless).

### Streaming interaction
Streaming appends to the **latest** version's `Text`. During streaming `DisplayedVersion == LatestVersion`
(snap guarantees it), so the live growth shows exactly as today. If the user has paged back to an older
version and a new version arrives, `CreateNewVersion`'s snap pulls them forward to the new one — the
expected "something new happened" behavior. While paged back with no new version, the older version is
static (correct).

### Copy
`CopyLatest` becomes copy-the-**displayed**-version (`DisplayedVersion?.Text`) so Copy matches what the
user is looking at. (Renamed conceptually; command name may stay `CopyLatest` to avoid touching the XAML
binding, or rename to `CopyDisplayed` — implementer's choice, keep it one rename.)

### XAML
A `‹  v2 / 3  ›` pager placed on the existing version-label line (the line that already shows
`VersionLabel · TimeLabel`):
- Visible only when `HasMultipleVersions` (else collapsed — single-version cards look exactly as today).
- `‹` button → `ShowOlderVersionCommand`, `IsEnabled="{Binding CanShowOlder}"`.
- `›` button → `ShowNewerVersionCommand`, `IsEnabled="{Binding CanShowNewer}"`.
- `VersionPositionLabel` text between them.
- The answer Grid's three branches (streaming mono, complete markdown, error) rebind their
  `LatestVersion.*` paths to `DisplayedVersion.*`.

---

## §7 — Streaming-completion caret

An explicit "still streaming" → "done" cue beyond the busy bar.

- A blinking caret glyph (`▋`) rendered at the end of the live mono text, **visible only while the
  displayed version is streaming** (`IsComplete == false && IsError == false`) — i.e. the same
  condition that shows the raw streaming `TextBlock`. It disappears the instant `IsComplete` flips to
  the rendered-markdown branch, marking completion.
- Pure XAML: a caret element beside the streaming text + an opacity blink `Storyboard`
  (`DoubleAnimation` 1→0→1, ~1s, `RepeatBehavior=Forever`, `AutoReverse`), started by an
  `EventTrigger`/`DataTrigger` tied to the streaming-visible condition. No new VM members — reuses the
  existing `IsComplete` / `IsError` booleans.

---

## §8 — Minimal UIA names

`AutomationProperties.Name` on three card elements, doubling as stable UITest targets:
- Question text → `"Question"`.
- Status line → `"Answer status"`.
- Answer container (the Grid) → `"Answer"`.

No live-region / busy announcements (low value on a deliberately stealthy overlay, per the review).

---

## Data flow & error handling
Pipeline and answer wiring are untouched. `OnChunk` / `OnComplete` / `OnError` continue to write the
**latest** version. All new behavior is display-only and bounds-clamped. The pager is hidden when there
are fewer than two versions or `DisplayedVersion` is null; navigation commands clamp at both ends.

## Testing
Follows the established App-layer convention from items 1–4: presentation bindings are not unit-tested
(there is no VM test project); verified by `dotnet build` + manual run on the live overlay. The pager
index math is small and clamped.

**Optional** (not a blocker): one FlaUI UITest — start a session, trigger a turn, `Regen` to create a
second version, assert the `TurnCard` pager is visible and `VersionPositionLabel` reads `"v2 / 2"`.
Added only if it proves deterministic in the existing UITest harness; otherwise manual verification
stands.

## Out of scope
- §6 markdown 4-part rendering — already shipped (PR #30).
- Answer-depth scaling — separate spec (Spec B).
- Live-region accessibility announcements.
