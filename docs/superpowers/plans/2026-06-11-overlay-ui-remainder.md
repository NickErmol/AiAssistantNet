# Overlay UI Remainder Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close out the deferred overlay-UI items from the 2026-06-09 review — a §5 version pager, a §7 streaming-completion caret, and §8 minimal UIA names — without touching the pipeline, Domain, Infrastructure, or EF.

**Architecture:** App-layer only. `TurnVm` gains a *displayed* version distinct from *latest* plus derived pager state; `ConversationTurnViewModel` gains two navigation `RelayCommand`s; `MainOverlayWindow.xaml` rebinds the answer area from `LatestVersion` to `DisplayedVersion`, adds a `‹ v2/3 ›` pager, a blinking trailing caret while streaming, and three `AutomationProperties.Name`s. A new `AIHelperNET.App.Tests` project unit-tests the pager navigation logic (the only piece with real logic).

**Tech Stack:** WPF + CommunityToolkit.Mvvm (source-gen `[RelayCommand]`/`[ObservableProperty]`), existing `BoolToVisibilityConverter`, `IconBtn` style, `Brush.*`/`Font.*` resources; xUnit + FluentAssertions + NSubstitute for the new tests.

> **Testing reality:** the pager navigation logic in `TurnVm` is unit-tested in the new `AIHelperNET.App.Tests` project (TDD, Tasks 1–3). The XAML bindings, blinking caret, and UIA names cannot be meaningfully unit-tested — they are verified by `dotnet build` (compile gate, `TreatWarningsAsErrors` is on) plus manual checks on the running overlay. Task 9 adds the *one* optional FlaUI UITest if it proves deterministic.

**Build commands (use throughout):**
- App: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
- New tests: `dotnet test tests/AIHelperNET.App.Tests`
- Full: `dotnet build`

**Run command (manual checks):** the app is launched via the `run-aihelper` skill (stop → build → start). Re-run it after each XAML task to see the change.

**Namespaces (for the test files):**
- `AIHelperNET.App.ViewModels` — `TurnVm`, `AnswerVersionVm`, `ConversationTurnViewModel`
- `AIHelperNET.Domain.Ids` — `ConversationTurnId`, `AnswerVersionId`
- `AIHelperNET.Domain.Sessions` — `AnswerVersionType`
- `AIHelperNET.Application.Answers` — `ScreenTaskContextStore`
- `Mediator` — `IMediator`

---

## File Structure

- `tests/AIHelperNET.App.Tests/AIHelperNET.App.Tests.csproj` — **create**. New xUnit project, TFM
  `net10.0-windows10.0.17763.0`, references `src/AIHelperNET.App`.
- `tests/AIHelperNET.App.Tests/TurnVmVersionPagerTests.cs` — **create**. Pure `TurnVm` navigation tests.
- `tests/AIHelperNET.App.Tests/ConversationTurnViewModelSnapTests.cs` — **create**. Snap-on-new test.
- `AIHelperNET.slnx` — **modify**. Add the new test project.
- `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` — **modify**. Add displayed-version state
  + derived pager props + navigation methods to `TurnVm`; add two navigation commands and the
  `CreateNewVersion` snap + `CopyLatest` change to the parent.
- `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` — **modify**. Rebind the answer/version-label area
  to `DisplayedVersion`, add the pager, the streaming caret, and the UIA names.
- `tests/AIHelperNET.UITests/...` — **optional** (Task 9). One pager UITest.

---

## Task 1: Scaffold `AIHelperNET.App.Tests` + write the failing navigation tests (RED)

**Files:**
- Create: `tests/AIHelperNET.App.Tests/AIHelperNET.App.Tests.csproj`
- Create: `tests/AIHelperNET.App.Tests/TurnVmVersionPagerTests.cs`
- Modify: `AIHelperNET.slnx`

- [ ] **Step 1: Create the test project file.**

`tests/AIHelperNET.App.Tests/AIHelperNET.App.Tests.csproj` (mirrors the other test csprojs; TFM matches
the App project; `CS1591` suppressed because the App project emits XML docs and tests need none):

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591;CA1707;CA1515</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="Microsoft.Extensions.TimeProvider.Testing" Version="10.6.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AIHelperNET.App\AIHelperNET.App.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the project to the solution.**

Run: `dotnet sln AIHelperNET.slnx add tests/AIHelperNET.App.Tests/AIHelperNET.App.Tests.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 3: Write the failing `TurnVm` navigation tests.**

`tests/AIHelperNET.App.Tests/TurnVmVersionPagerTests.cs` — these reference `TurnVm.DisplayedVersion`,
`HasMultipleVersions`, `VersionPositionLabel`, `CanShowOlder`, `CanShowNewer`, `ShowOlder()`,
`ShowNewer()`, none of which exist yet (compile-fail = RED):

```csharp
using AIHelperNET.App.ViewModels;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests;

public class TurnVmVersionPagerTests
{
    private static AnswerVersionVm V() =>
        new(AnswerVersionId.New(), AnswerVersionType.Preliminary, DateTimeOffset.UtcNow);

    // Builds a turn with `count` versions, newest-first (index 0 = newest), displayed = newest —
    // matching how CreateNewVersion populates the collection in production.
    private static TurnVm TurnWith(int count)
    {
        var turn = new TurnVm(ConversationTurnId.New(), "q");
        for (var i = 0; i < count; i++)
            turn.AnswerVersions.Insert(0, V());
        turn.DisplayedVersion = turn.AnswerVersions.Count > 0 ? turn.AnswerVersions[0] : null;
        return turn;
    }

    [Fact]
    public void SingleVersion_HasNoPager()
    {
        var t = TurnWith(1);
        t.HasMultipleVersions.Should().BeFalse();
        t.VersionPositionLabel.Should().Be("v1 / 1");
        t.CanShowOlder.Should().BeFalse();
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void NewestDisplayed_ShowsHighestPosition_CanGoOlderOnly()
    {
        var t = TurnWith(3);
        t.HasMultipleVersions.Should().BeTrue();
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.CanShowOlder.Should().BeTrue();
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void ShowOlder_StepsBackChronologically_AndClampsAtOldest()
    {
        var t = TurnWith(3);
        t.ShowOlder();
        t.VersionPositionLabel.Should().Be("v2 / 3");
        t.CanShowNewer.Should().BeTrue();
        t.ShowOlder();
        t.VersionPositionLabel.Should().Be("v1 / 3");
        t.CanShowOlder.Should().BeFalse();
        t.ShowOlder(); // clamp — no further movement
        t.VersionPositionLabel.Should().Be("v1 / 3");
    }

    [Fact]
    public void ShowNewer_StepsForward_AndClampsAtNewest()
    {
        var t = TurnWith(3);
        t.ShowOlder();
        t.ShowOlder(); // now at v1
        t.ShowNewer();
        t.VersionPositionLabel.Should().Be("v2 / 3");
        t.ShowNewer();
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.ShowNewer(); // clamp
        t.VersionPositionLabel.Should().Be("v3 / 3");
        t.CanShowNewer.Should().BeFalse();
    }

    [Fact]
    public void NoVersions_PagerEmpty_AndNavigationIsSafeNoOp()
    {
        var t = new TurnVm(ConversationTurnId.New(), "q");
        t.HasMultipleVersions.Should().BeFalse();
        t.VersionPositionLabel.Should().BeEmpty();
        t.CanShowOlder.Should().BeFalse();
        t.CanShowNewer.Should().BeFalse();
        t.ShowOlder(); // must not throw
        t.ShowNewer();
    }
}
```

- [ ] **Step 4: Build the test project to confirm RED (compile failure on missing members).**

Run: `dotnet build tests/AIHelperNET.App.Tests`
Expected: FAIL — errors like `'TurnVm' does not contain a definition for 'DisplayedVersion'`.

- [ ] **Step 5: Commit (RED test + scaffold).**

```bash
git add tests/AIHelperNET.App.Tests AIHelperNET.slnx
git commit -m "test(overlay): scaffold App.Tests + failing TurnVm pager nav tests"
```

---

## Task 2: Implement `TurnVm` displayed-version state + navigation (GREEN)

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` (the `TurnVm` class, currently lines ~97–186)

- [ ] **Step 1: Add the members the tests need to `TurnVm`.**

Insert immediately after the existing `LatestVersion` property (after line ~177, before the
`_followUpText` field):

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

- [ ] **Step 2: Run the navigation tests — expect GREEN.**

Run: `dotnet test tests/AIHelperNET.App.Tests`
Expected: all 5 `TurnVmVersionPagerTests` PASS.

- [ ] **Step 3: Commit.**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat(overlay): add displayed-version state + pager nav to TurnVm"
```

---

## Task 3: Snap on new version, navigation commands, Copy-displayed (+ snap test)

**Files:**
- Create: `tests/AIHelperNET.App.Tests/ConversationTurnViewModelSnapTests.cs`
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` (`CreateNewVersion`, `CopyLatest`, parent command region)

- [ ] **Step 1: Write the failing snap test (RED — `CreateNewVersion` does not set `DisplayedVersion` yet).**

`tests/AIHelperNET.App.Tests/ConversationTurnViewModelSnapTests.cs`:

```csharp
using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

public class ConversationTurnViewModelSnapTests
{
    private static ConversationTurnViewModel NewVm() =>
        new(Substitute.For<IMediator>(), TimeProvider.System, new ScreenTaskContextStore());

    [Fact]
    public void NewVersion_SnapsDisplayedToNewest_EvenWhenPagedBack()
    {
        var vm = NewVm();
        var turnId = ConversationTurnId.New();
        vm.AddTurn(turnId, "q");
        var turn = vm.GetTurn(turnId)!;

        vm.OnChunk(turnId, AnswerVersionType.Preliminary, "first"); // v1, displayed = v1
        vm.OnError(turnId, "boom");                                 // v2 (newest), should snap forward
        turn.DisplayedVersion.Should().BeSameAs(turn.AnswerVersions[0]);
        turn.VersionPositionLabel.Should().Be("v2 / 2");

        turn.ShowOlder();                                           // page back to v1
        turn.VersionPositionLabel.Should().Be("v1 / 2");

        vm.OnError(turnId, "boom2");                                // v3 (newest), snap forward again
        turn.DisplayedVersion.Should().BeSameAs(turn.AnswerVersions[0]);
        turn.VersionPositionLabel.Should().Be("v3 / 3");
    }
}
```

- [ ] **Step 2: Confirm RED.**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~Snap"`
Expected: FAIL — after `OnError`, `DisplayedVersion` is still the old version (snap not implemented),
so `VersionPositionLabel` is wrong.

- [ ] **Step 3: Add the snap to `CreateNewVersion`.**

In `CreateNewVersion` (currently lines ~266–275), after `turn.LatestVersion = version;` add the snap:

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

- [ ] **Step 4: Make `CopyLatest` copy the displayed version.**

Change `CopyLatest` (currently lines ~335–341) to read `DisplayedVersion`:

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

- [ ] **Step 5: Add the two navigation commands to `ConversationTurnViewModel`.**

Add next to the other `[RelayCommand]` members (e.g. right after `RegenerateAsync`, ~line 299):

```csharp
    /// <summary>Pages the given turn's card to the next older answer version.</summary>
    [RelayCommand]
    private static void ShowOlderVersion(TurnVm? turn) => turn?.ShowOlder();

    /// <summary>Pages the given turn's card to the next newer answer version.</summary>
    [RelayCommand]
    private static void ShowNewerVersion(TurnVm? turn) => turn?.ShowNewer();
```

- [ ] **Step 6: Run all App.Tests — expect GREEN; confirms the generated `ShowOlderVersionCommand` / `ShowNewerVersionCommand` exist for the XAML.**

Run: `dotnet test tests/AIHelperNET.App.Tests`
Expected: all tests PASS (5 nav + 1 snap).

- [ ] **Step 7: Commit.**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs tests/AIHelperNET.App.Tests/ConversationTurnViewModelSnapTests.cs
git commit -m "feat(overlay): snap to new version, add pager commands, copy displayed version"
```

---

## Task 4: Rebind the answer area to `DisplayedVersion`

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (the turn-card `DataTemplate`, currently lines ~404–473)

- [ ] **Step 1: Repoint every `LatestVersion.*` binding in the version-label line and answer Grid to `DisplayedVersion.*`.**

Six binding paths to change inside the `DataTemplate` (leave the action-row Copy/Regen bindings, which
bind the `TurnVm` itself, untouched):

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

Use Edit per binding. Do **not** change `CommandParameter="{Binding}"` on the action buttons.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual check — answer still renders normally.**

Re-run via `run-aihelper`. Start a session, get one answer. Expected: the card shows the streaming text
then the rendered markdown exactly as before (single-version cards unchanged; `DisplayedVersion` ==
`LatestVersion` here). No pager yet.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "refactor(overlay): bind answer area to DisplayedVersion"
```

---

## Task 5: Version pager UI

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
version. Expected: the `‹ v2 / 2 ›` pager appears; `‹` enabled, `›` disabled. Click `‹` → older answer
shows, position `v1 / 2`, now `›` enabled and `‹` disabled. Click `›` → back to `v2 / 2`. A brand-new
question's card (one version) shows **no** pager.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): version pager on the turn card"
```

---

## Task 6: §7 streaming-completion caret

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (streaming `TextBlock`, currently lines ~421–441)

- [ ] **Step 1: Add a blinking trailing caret to the streaming `TextBlock` via an `InlineUIContainer`.**

The streaming branch `TextBlock` currently has `Text="{Binding DisplayedVersion.Text}"`. Replace that
single `Text=` attribute with inline content so a caret flows after the last character (an
`InlineUIContainer` hosts a real `UIElement`, so its `Opacity` is animatable — a `Run` cannot blink).
Drop the `Text="…"` attribute from the opening tag and add the inline body:

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

Notes:
- Keep the `FontSize` binding to `AnswerFontSize` exactly as on the original streaming `TextBlock`.
- The caret lives in the streaming branch, which is only `Visible` while `IsComplete=false && IsError=false`,
  so it shows only during streaming and disappears the instant the card swaps to rendered markdown.
- No `x:Name` needed; the storyboard with no target name animates the caret `TextBlock` its trigger is on.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Manual check — caret blinks while streaming, vanishes on completion.**

Re-run via `run-aihelper`. Ask a question. Expected: while tokens stream, a `▋` caret blinks at the end
of the growing text; when the answer finishes and rendered markdown appears, the caret is gone.

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): blinking streaming caret as completion cue"
```

---

## Task 7: §8 minimal UIA names

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (question `TextBlock` ~line 361, status `TextBlock` ~line 369, answer `Grid` ~line 419)

- [ ] **Step 1: Add `AutomationProperties.Name` to the three card elements.**

- Question text `TextBlock` (bound to `InitialQuestion`, ~line 361): add `AutomationProperties.Name="Question"`.
- Status-line `TextBlock` (bound to `StatusLabel`, ~line 369): add `AutomationProperties.Name="Answer status"`.
- Answer `Grid` (the `<Grid Margin="0,0,0,4">` wrapping streaming/markdown/error, ~line 419): add
  `AutomationProperties.Name="Answer"`.

- [ ] **Step 2: Build.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 3: Commit.**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(overlay): minimal UIA names on question/status/answer"
```

---

## Task 8: Full build + regression test sweep

- [ ] **Step 1: Full solution build.**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the fast test projects to confirm no regressions.**

Run: `dotnet test tests/AIHelperNET.App.Tests tests/AIHelperNET.Application.Tests tests/AIHelperNET.Domain.Tests`
Expected: all PASS (App.Tests 6; Application/Domain unchanged from baseline 187/104).

- [ ] **Step 3: Commit nothing (verification only) — proceed.**

---

## Task 9 (OPTIONAL): Pager UITest

Only attempt if it proves deterministic in the existing FlaUI harness; otherwise skip — the unit tests
(Tasks 1–3) plus manual verification cover the logic. Do not block the branch on this.

**Files:**
- Inspect first: an existing test under `tests/AIHelperNET.UITests/` that starts a session and reaches a
  turn card (reuse its setup/launch helpers — the UITests drive a **prebuilt** `App.exe`, so rebuild first).
- Modify/Create: a new `[Fact]` in the most relevant existing UITest class.

- [ ] **Step 1: Write the test using the existing harness helpers.**

Mirror an existing UITest's session-start + turn-detection helper, then:

```csharp
// after a turn card is present and answered:
var regen = card.FindFirstDescendant(cf => cf.ByName("Regen")).AsButton();
regen.Invoke();
// wait for the second version (reuse the harness's Retry/WaitUntil helper):
var pager = card.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard_VersionPager"));
pager.Should().NotBeNull("a second version must show the pager");
var pos = card.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard_VersionPosition")).AsLabel();
pos.Text.Should().Be("v2 / 2");
```

- [ ] **Step 2: Rebuild the app, then run only this test.**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Run: `dotnet test tests/AIHelperNET.UITests --filter "FullyQualifiedName~VersionPager"`
Expected: PASS. If it flakes on timing or can't deterministically produce a 2nd version, delete the test
and rely on the unit tests + manual verification (note this in the PR).

- [ ] **Step 3: Commit (only if kept).**

```bash
git add tests/AIHelperNET.UITests
git commit -m "test(ui): pager appears with v2/2 after regen"
```

---

## Final verification & wrap-up

- [ ] **Step 1: Manual end-to-end on the live overlay (use the `verify` / `run-aihelper` flow).**

Confirm in one run: single-version card unchanged (no pager); Regen → `‹ v2/2 ›` pager navigates and
Copy copies the *displayed* version; streaming caret blinks then disappears on completion; error card
still renders in the error color (regression check). Toggle stealth/theme to confirm no layout breakage.

- [ ] **Step 2: Use the `finishing-a-development-branch` skill** to merge/PR `feature/overlay-ui-remainder`
  into `develop` (gitflow). This repo auto-merges PRs on creation — push all commits first.

---

## Self-review (done while writing)

- **Spec coverage:** §5 → Tasks 1–5 (logic + tests + XAML). §7 → Task 6. §8 → Task 7. Copy-displayed →
  Task 3. App.Tests project → Tasks 1–3. Optional UITest → Task 9. All spec sections mapped.
- **Type consistency:** `DisplayedVersion`, `HasMultipleVersions`, `VersionPositionLabel`,
  `CanShowOlder`, `CanShowNewer`, `ShowOlder()`/`ShowNewer()`, generated
  `ShowOlderVersionCommand`/`ShowNewerVersionCommand`, and `CopyLatestCommand` (name preserved) match
  across the VM, the tests, and the XAML bindings.
- **No placeholders:** every code/XAML/test step shows actual content; the only "optional" is Task 9,
  explicitly gated.
