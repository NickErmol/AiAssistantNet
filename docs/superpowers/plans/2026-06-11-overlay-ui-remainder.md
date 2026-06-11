# Overlay UI Remainder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the deferred overlay-UI items from the 2026-06-09 review — a §5 version pager, a §7 streaming-completion caret, and §8 minimal UIA names — without touching the pipeline, Domain, Infrastructure, or EF.

**Architecture:** App-layer only. `TurnVm` gains a *displayed* version distinct from *latest* plus derived pager state; `ConversationTurnViewModel` gains two navigation `RelayCommand`s; `MainOverlayWindow.xaml` rebinds the answer area from `LatestVersion` to `DisplayedVersion`, adds a `‹ v2/3 ›` pager, a blinking trailing caret while streaming, and three `AutomationProperties.Name`s.

**Tech Stack:** WPF + CommunityToolkit.Mvvm (source-gen `[RelayCommand]`/`[ObservableProperty]`), existing `BoolToVisibilityConverter`, `IconBtn` style, `Brush.*`/`Font.*` resources.

> **Testing reality:** there is no VM/App unit-test project in this repo (Domain/Application/Infrastructure/Integration/UITests/RealAudio only). Per the convention items 1–4 of the review followed, presentation logic here is verified by `dotnet build` (compile gate) plus manual checks on the running overlay. Task 7 adds the *one* optional FlaUI UITest if it proves deterministic. Do **not** invent a VM unit-test project.

**Build command (use throughout):**
`dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
(`Directory.Build.props` sets `TreatWarningsAsErrors` — a warning fails the build.)

**Run command (manual checks):** the app is launched via the `run-aihelper` skill (stop → build → start). Re-run it after each XAML task to see the change.

---

## File Structure

- `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` — **modify**. Holds both
  `AnswerVersionVm` and `TurnVm` and the parent `ConversationTurnViewModel`. Add displayed-version
  state + derived pager props + navigation methods to `TurnVm`; add two navigation commands and the
  `CreateNewVersion` snap + `CopyLatest` change to the parent.
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` — **modify**. Rebind the answer/version-label
  area to `DisplayedVersion`, add the pager, the streaming caret, and the UIA names.
- `tests/AIHelperNET.UITests/...` — **optional** (Task 7). One pager UITest.

---

## Task 1: `TurnVm` displayed-version state + navigation

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` (the `TurnVm` class, currently lines ~97–186)

- [ ] **Step 1: Add displayed-version field, property, derived pager members, and navigation methods to `TurnVm`.**

Insert the following members into `TurnVm`, immediately after the existing `LatestVersion` property
(after line ~177, before the `_followUpText` field):

```csharp
    private AnswerVersionVm? _displayedVersion;
    /// <summary>Gets or sets the version currently shown on the card. Distinct from
    /// <see cref="LatestVersion"/>: the user can page back to a superseded version, but creating a new
    /// version snaps this forward to the newest. Drives the answer area and the version pager.</summary>
    public AnswerVersionVm? DisplayedVersion
    {
        get => _displayedVersion;
        set
        {
            if (SetProperty(ref _displayedVersion, value))
                RaiseVersionNav();
        }
    }

    /// <summary>Gets a value indicating whether more than one answer version exists (drives pager visibility).</summary>
    public bool HasMultipleVersions => AnswerVersions.Count > 1;

    /// <summary>Gets the pager label in chronological order, e.g. "v2 / 3" (v1 = oldest). Empty when no
    /// version is displayed.</summary>
    public string VersionPositionLabel
    {
        get
        {
            if (DisplayedVersion is null) return string.Empty;
            var idx = AnswerVersions.IndexOf(DisplayedVersion);
            if (idx < 0) return string.Empty;
            // List is newest-first (index 0 = newest), so chronological position = Count - idx.
            return $"v{AnswerVersions.Count - idx} / {AnswerVersions.Count}";
        }
    }

    /// <summary>Gets a value indicating whether a strictly older version exists to page back to.</summary>
    public bool CanShowOlder =>
        DisplayedVersion is not null &&
        AnswerVersions.IndexOf(DisplayedVersion) < AnswerVersions.Count - 1;

    /// <summary>Gets a value indicating whether a strictly newer version exists to page forward to.</summary>
    public bool CanShowNewer =>
        DisplayedVersion is not null &&
        AnswerVersions.IndexOf(DisplayedVersion) > 0;

    private void RaiseVersionNav()
    {
        OnPropertyChanged(nameof(HasMultipleVersions));
        OnPropertyChanged(nameof(VersionPositionLabel));
        OnPropertyChanged(nameof(CanShowOlder));
        OnPropertyChanged(nameof(CanShowNewer));
    }

    /// <summary>Pages the displayed version one step toward the older end (clamped).</summary>
    public void ShowOlder()
    {
        if (DisplayedVersion is null) return;
        var idx = AnswerVersions.IndexOf(DisplayedVersion);
        if (idx >= 0 && idx < AnswerVersions.Count - 1)
            DisplayedVersion = AnswerVersions[idx + 1];
    }

    /// <summary>Pages the displayed version one step toward the newer end (clamped).</summary>
    public void ShowNewer()
    {
        if (DisplayedVersion is null) return;
        var idx = AnswerVersions.IndexOf(DisplayedVersion);
        if (idx > 0)
            DisplayedVersion = AnswerVersions[idx - 1];
    }
```

- [ ] **Step 2: Build to verify it compiles.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit.**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat(overlay): add displayed-version state + pager nav to TurnVm"
```

---

## Task 2: Snap on new version, navigation commands, Copy-displayed

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` (`CreateNewVersion`, `CopyLatest`, and the parent `ConversationTurnViewModel` command region)

- [ ] **Step 1: Snap `DisplayedVersion` to the new version in `CreateNewVersion`.**

In `CreateNewVersion` (currently lines ~266–275), after `turn.LatestVersion = version;` add the snap so
a fresh preliminary/refine/screen/follow-up jumps the card forward (preserving today's UX):

```csharp
    private static AnswerVersionVm CreateNewVersion(
        TurnVm turn, AnswerVersionId id, AnswerVersionType type, string text = "", bool isError = false)
    {
        foreach (var v in turn.AnswerVersions) v.IsLatest = false;
        var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
            { Text = text, IsLatest = true, IsError = isError, IsComplete = isError };
        turn.AnswerVersions.Insert(0, version);
        turn.LatestVersion = version;
        turn.DisplayedVersion = version;   // snap forward; also raises pager nav (Count now updated)
        return version;
    }
```

- [ ] **Step 2: Make `CopyLatest` copy the displayed version.**

Change `CopyLatest` (currently lines ~335–341) to read `DisplayedVersion` so Copy matches what's shown
(command name kept to avoid touching the XAML binding):

```csharp
    /// <summary>Copies the currently displayed answer version's text to the clipboard.</summary>
    [RelayCommand]
    private static void CopyLatest(TurnVm? turn)
    {
        var text = turn?.DisplayedVersion?.Text;
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
    }
```

- [ ] **Step 3: Add the two navigation commands to `ConversationTurnViewModel`.**

Add next to the other `[RelayCommand]` members (e.g. right after `RegenerateAsync`, ~line 299). They
delegate to the `TurnVm` methods from Task 1:

```csharp
    /// <summary>Pages the given turn's card to the next older answer version.</summary>
    [RelayCommand]
    private static void ShowOlderVersion(TurnVm? turn) => turn?.ShowOlder();

    /// <summary>Pages the given turn's card to the next newer answer version.</summary>
    [RelayCommand]
    private static void ShowNewerVersion(TurnVm? turn) => turn?.ShowNewer();
```

- [ ] **Step 4: Build to verify it compiles (confirms the source-gen commands `ShowOlderVersionCommand` / `ShowNewerVersionCommand` exist for the XAML to bind).**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit.**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat(overlay): snap to new version, add pager commands, copy displayed version"
```

---

## Task 3: Rebind the answer area to `DisplayedVersion`

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (the turn-card `DataTemplate`, currently lines ~404–473)

- [ ] **Step 1: Repoint every `LatestVersion.*` binding in the version-label line and answer Grid to `DisplayedVersion.*`.**

There are exactly six binding paths to change inside the `DataTemplate` (leave the action-row Copy/Regen
bindings, which bind the `TurnVm` itself, untouched):

1. Version-label line (line ~408): `LatestVersion.VersionLabel` → `DisplayedVersion.VersionLabel`,
   and `LatestVersion.TimeLabel` → `DisplayedVersion.TimeLabel`.
2. Version-label line collapse trigger (line ~412): `Binding LatestVersion` → `Binding DisplayedVersion`.
3. Streaming `TextBlock` text (line ~421): `LatestVersion.Text` → `DisplayedVersion.Text`.
4. Streaming visibility `MultiDataTrigger` conditions (lines ~433–434):
   `LatestVersion.IsComplete` → `DisplayedVersion.IsComplete`, `LatestVersion.IsError` → `DisplayedVersion.IsError`.
5. `MarkdownPresenter` (lines ~444–445 and trigger ~454–455):
   `LatestVersion.RenderedMarkdown` → `DisplayedVersion.RenderedMarkdown`;
   trigger `LatestVersion.IsComplete`/`LatestVersion.IsError` → `DisplayedVersion.*`.
6. Error `TextBlock` (lines ~465 and ~471): `LatestVersion.Text` → `DisplayedVersion.Text`,
   `LatestVersion.IsError` → `DisplayedVersion.IsError`.

Use Edit per binding (each old string is unique enough with its surrounding attribute). Do **not** change
`CommandParameter="{Binding}"` on the action buttons.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual check — answer still renders normally.**

Re-run via `run-aihelper`. Start a session, get one answer. Expected: the card shows the streaming text
then the rendered markdown exactly as before (single-version cards are visually unchanged; `DisplayedVersion`
== `LatestVersion` here). No pager yet.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "refactor(overlay): bind answer area to DisplayedVersion"
```

---

## Task 4: Version pager UI

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (version-label line, currently lines ~404–418)

- [ ] **Step 1: Wrap the version-label `TextBlock` and a pager in a horizontal `StackPanel`.**

Replace the whole version-label `TextBlock` block (the element spanning lines ~405–418, the
`<TextBlock FontSize="{DynamicResource Font.XS}" … Margin="0,0,0,4"> … </TextBlock>` that renders
`DisplayedVersion.VersionLabel · TimeLabel`) with:

```xml
<StackPanel Orientation="Horizontal" Margin="0,0,0,4">
    <TextBlock FontSize="{DynamicResource Font.XS}"
               VerticalAlignment="Center"
               Foreground="{DynamicResource Brush.Foreground.Muted}">
        <Run Text="{Binding DisplayedVersion.VersionLabel, Mode=OneWay}"/><Run Text=" · "/><Run Text="{Binding DisplayedVersion.TimeLabel, Mode=OneWay}"/>
        <TextBlock.Style>
            <Style TargetType="TextBlock">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding DisplayedVersion}" Value="{x:Null}">
                        <Setter Property="Visibility" Value="Collapsed"/>
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </TextBlock.Style>
    </TextBlock>

    <!-- Version pager: visible only when more than one version exists -->
    <StackPanel Orientation="Horizontal"
                Margin="10,0,0,0"
                VerticalAlignment="Center"
                AutomationProperties.AutomationId="TurnCard_VersionPager"
                Visibility="{Binding HasMultipleVersions, Converter={StaticResource BoolToVisibilityConverter}}">
        <Button Content="‹"
                AutomationProperties.AutomationId="TurnCard_VersionOlder"
                Style="{StaticResource IconBtn}"
                FontSize="12"
                IsEnabled="{Binding CanShowOlder}"
                Command="{Binding DataContext.ConversationTurn.ShowOlderVersionCommand,
                    RelativeSource={RelativeSource AncestorType=Window}}"
                CommandParameter="{Binding}"
                ToolTip="Older version"/>
        <TextBlock Text="{Binding VersionPositionLabel}"
                   AutomationProperties.AutomationId="TurnCard_VersionPosition"
                   VerticalAlignment="Center"
                   Margin="3,0"
                   FontSize="{DynamicResource Font.XS}"
                   Foreground="{DynamicResource Brush.Foreground.Muted}"/>
        <Button Content="›"
                AutomationProperties.AutomationId="TurnCard_VersionNewer"
                Style="{StaticResource IconBtn}"
                FontSize="12"
                IsEnabled="{Binding CanShowNewer}"
                Command="{Binding DataContext.ConversationTurn.ShowNewerVersionCommand,
                    RelativeSource={RelativeSource AncestorType=Window}}"
                CommandParameter="{Binding}"
                ToolTip="Newer version"/>
    </StackPanel>
</StackPanel>
```

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual check — pager appears and navigates.**

Re-run via `run-aihelper`. Start a session, get an answer, then click **Regen** to create a second
version. Expected: the `‹ v2 / 2 ›` pager appears; `‹` is enabled and `›` disabled. Click `‹` → the card
shows the older answer and label, position reads `v1 / 2`, now `›` is enabled and `‹` disabled. Click `›`
→ back to `v2 / 2`. A brand-new question's card (one version) shows **no** pager.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): version pager on the turn card"
```

---

## Task 5: §7 streaming-completion caret

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (streaming `TextBlock`, currently lines ~421–441)

- [ ] **Step 1: Add a blinking trailing caret to the streaming `TextBlock` via an `InlineUIContainer`.**

The streaming branch `TextBlock` currently has `Text="{Binding DisplayedVersion.Text}"`. Replace that
single `Text=` attribute binding with inline content so a caret flows after the last character (an
`InlineUIContainer` hosts a real `UIElement`, so its `Opacity` is animatable — a `Run` cannot blink).
Change the opening tag to drop the `Text="…"` attribute and add inline content as the element body:

```xml
<TextBlock TextWrapping="Wrap"
           FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                              RelativeSource={RelativeSource AncestorType=Window}}"
           FontFamily="Cascadia Mono, Consolas"
           Foreground="{DynamicResource Brush.Foreground.Primary}">
    <Run Text="{Binding DisplayedVersion.Text}"/><InlineUIContainer BaselineAlignment="TextBottom"><TextBlock Text="▋"
               Foreground="{DynamicResource Brush.Semantic.Busy}"
               FontFamily="Cascadia Mono, Consolas">
        <TextBlock.Triggers>
            <EventTrigger RoutedEvent="TextBlock.Loaded">
                <BeginStoryboard>
                    <Storyboard RepeatBehavior="Forever" AutoReverse="True">
                        <DoubleAnimation Storyboard.TargetProperty="Opacity"
                                         From="1" To="0" Duration="0:0:0.6"/>
                    </Storyboard>
                </BeginStoryboard>
            </EventTrigger>
        </TextBlock.Triggers>
    </TextBlock></InlineUIContainer>
    <TextBlock.Style>
        <Style TargetType="TextBlock">
            <Setter Property="Visibility" Value="Collapsed"/>
            <Style.Triggers>
                <MultiDataTrigger>
                    <MultiDataTrigger.Conditions>
                        <Condition Binding="{Binding DisplayedVersion.IsComplete}" Value="False"/>
                        <Condition Binding="{Binding DisplayedVersion.IsError}" Value="False"/>
                    </MultiDataTrigger.Conditions>
                    <Setter Property="Visibility" Value="Visible"/>
                </MultiDataTrigger>
            </Style.Triggers>
        </Style>
    </TextBlock.Style>
</TextBlock>
```

Notes for the implementer:
- Keep the `FontSize` binding to `AnswerFontSize` exactly as it was on the original streaming `TextBlock`.
- The caret is inside the streaming branch, which is only `Visible` while `IsComplete=false && IsError=false`,
  so it shows only during streaming and disappears the instant the card swaps to the rendered-markdown
  branch on completion.
- `x:Name` is not needed; `Storyboard.TargetProperty="Opacity"` with no target name animates the caret
  `TextBlock` the trigger is attached to.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual check — caret blinks while streaming, vanishes on completion.**

Re-run via `run-aihelper`. Ask a question. Expected: while tokens stream, a `▋` caret blinks at the end of
the growing text; the moment the answer finishes and the rendered markdown appears, the caret is gone.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): blinking streaming caret as completion cue"
```

---

## Task 6: §8 minimal UIA names

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (question `TextBlock` ~line 361, status `TextBlock` ~line 369, answer `Grid` ~line 419)

- [ ] **Step 1: Add `AutomationProperties.Name` to the three card elements.**

- On the question text `TextBlock` (the one bound to `InitialQuestion`, ~line 361), add:
  `AutomationProperties.Name="Question"`.
- On the status-line `TextBlock` (the one bound to `StatusLabel`, ~line 369), add:
  `AutomationProperties.Name="Answer status"`.
- On the answer `Grid` (the `<Grid Margin="0,0,0,4">` that wraps the streaming/markdown/error branches,
  ~line 419), add: `AutomationProperties.Name="Answer"`.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): minimal UIA names on question/status/answer"
```

---

## Task 7 (OPTIONAL): Pager UITest

Only attempt if it proves deterministic in the existing FlaUI harness; otherwise skip — manual
verification from Tasks 4–5 stands. Do not block the branch on this.

**Files:**
- Inspect first: an existing test under `tests/AIHelperNET.UITests/` that starts a session and reaches a
  turn card (reuse its setup/launch helpers — the UITests drive a **prebuilt** `App.exe`, so rebuild first).
- Modify/Create: a new `[Fact]` in the most relevant existing UITest class (e.g. the turn-card/answer test).

- [ ] **Step 1: Write the test using the existing harness helpers.**

Mirror an existing UITest's session-start + turn-detection helper, then:

```csharp
// after a turn card is present and answered:
var regen = card.FindFirstDescendant(cf => cf.ByName("Regen")).AsButton();
regen.Invoke();
// wait for the second version to materialize (reuse the harness's Retry/WaitUntil helper):
var pager = card.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard_VersionPager"));
pager.Should().NotBeNull("a second version must show the pager");
var pos = card.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard_VersionPosition")).AsLabel();
pos.Text.Should().Be("v2 / 2");
```

- [ ] **Step 2: Rebuild the app, then run only this test.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Run: `dotnet test tests/AIHelperNET.UITests --filter "FullyQualifiedName~VersionPager"`
Expected: PASS. If it flakes on timing or can't deterministically produce a 2nd version, delete the test
and rely on manual verification (note this in the PR).

- [ ] **Step 3: Commit (only if kept).**

```bash
git add tests/AIHelperNET.UITests
git commit -m "test(ui): pager appears with v2/2 after regen"
```

---

## Final verification & wrap-up

- [ ] **Step 1: Full solution build (catches anything cross-project).**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Manual end-to-end on the live overlay (use the `verify` / `run-aihelper` flow).**

Confirm in one run: single-version card unchanged (no pager); Regen → `‹ v2/2 ›` pager navigates and
Copy copies the *displayed* version; streaming caret blinks then disappears on completion; error card
still renders in the error color (regression check). Toggle stealth/theme to confirm no layout breakage.

- [ ] **Step 3: Use the `finishing-a-development-branch` skill** to merge/PR `feature/overlay-ui-remainder`
  into `develop` (gitflow). Remember this repo auto-merges PRs on creation — push all commits first.

---

## Self-review (done while writing)

- **Spec coverage:** §5 → Tasks 1,2,3,4. §7 → Task 5. §8 → Task 6. Copy-displayed → Task 2. Optional
  UITest → Task 7. All spec sections mapped.
- **Type consistency:** `DisplayedVersion`, `HasMultipleVersions`, `VersionPositionLabel`,
  `CanShowOlder`, `CanShowNewer`, `ShowOlder()`/`ShowNewer()`, and the generated
  `ShowOlderVersionCommand`/`ShowNewerVersionCommand` are named identically across the VM tasks and the
  XAML bindings. `CopyLatestCommand` name is preserved (no XAML change).
- **No placeholders:** every code/XAML step shows the actual content; the only "optional" is Task 7,
  explicitly gated.
