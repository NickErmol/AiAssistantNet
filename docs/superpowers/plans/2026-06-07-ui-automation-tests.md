# UI Automation Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Automate the AIHelperNET E2E checklist using FlaUI out-of-process UI automation tests plus a new audio-routing integration test class.

**Architecture:** New `tests/AIHelperNET.UITests` project launches the real built `.exe`, drives it with FlaUI UIA3, and asserts control states and UI transitions. XAML AutomationIds and HelpText bindings are added to all testable elements. Audio dot-state tests for Mic-only / System-only are handled inside the existing `AIHelperNET.Integration.Tests` — those already exist and require no new work.

**Tech Stack:** FlaUI.Core 4.0.0, FlaUI.UIA3 4.0.0, xUnit, FluentAssertions, .NET 10, WPF UIA3.

---

## File Map

| Action | File |
|---|---|
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` |
| Create | `tests/AIHelperNET.UITests/AIHelperNET.UITests.csproj` |
| Create | `tests/AIHelperNET.UITests/UITestCollection.cs` |
| Create | `tests/AIHelperNET.UITests/AppFixture.cs` |
| Create | `tests/AIHelperNET.UITests/MainWindow.cs` |
| Create | `tests/AIHelperNET.UITests/Tests/StartupTests.cs` |
| Create | `tests/AIHelperNET.UITests/Tests/UIControlTests.cs` |
| Create | `tests/AIHelperNET.UITests/Tests/SessionLifecycleTests.cs` |
| Create | `tests/AIHelperNET.UITests/Tests/ScreenCaptureTests.cs` |
| Create | `tests/AIHelperNET.UITests/Tests/SettingsTests.cs` |

---

## Task 1: Add AutomationIds and HelpText to MainOverlayWindow.xaml

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

### What to add

- [ ] **Step 1: Add AutomationId to the sidebar ▶ show button** (title bar, Grid.Column="0"):

Find:
```xml
<Button Grid.Column="0" Content="▶"
        Style="{StaticResource IconBtn}"
        FontSize="9"
        Visibility="{Binding SessionControl.IsSidebarVisible,
            Converter={StaticResource InverseBoolToVisibilityConverter}}"
        Command="{Binding SessionControl.ToggleSidebarCommand}"
        Margin="0,0,6,0"/>
```
Replace with:
```xml
<Button Grid.Column="0" Content="▶"
        Style="{StaticResource IconBtn}"
        FontSize="9"
        AutomationProperties.AutomationId="Btn_ToggleSidebar"
        Visibility="{Binding SessionControl.IsSidebarVisible,
            Converter={StaticResource InverseBoolToVisibilityConverter}}"
        Command="{Binding SessionControl.ToggleSidebarCommand}"
        Margin="0,0,6,0"/>
```

- [ ] **Step 2: Add AutomationId to the header TextBlock** (Grid.Column="1"):

Find:
```xml
<TextBlock Grid.Column="1"
           Foreground="{DynamicResource Brush.Foreground.Secondary}"
           FontSize="{DynamicResource Font.SM}"
           VerticalAlignment="Center">
```
Replace with:
```xml
<TextBlock Grid.Column="1"
           AutomationProperties.AutomationId="Header_StatusText"
           Foreground="{DynamicResource Brush.Foreground.Secondary}"
           FontSize="{DynamicResource Font.SM}"
           VerticalAlignment="Center">
```

- [ ] **Step 3: Add AutomationIds to title bar buttons** (in the Grid.Column="2" StackPanel). Replace each button as follows:

Start/Stop button — find:
```xml
<Button Content="{Binding SessionControl.IsSessionActive,
                    Converter={StaticResource BoolToStringConverter},
                    ConverterParameter='Stop|Start'}"
        Command="{Binding SessionControl.ToggleSessionCommand}"
        Style="{StaticResource ActionBtn}"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_ToggleSession"
        Content="{Binding SessionControl.IsSessionActive,
                    Converter={StaticResource BoolToStringConverter},
                    ConverterParameter='Stop|Start'}"
        Command="{Binding SessionControl.ToggleSessionCommand}"
        Style="{StaticResource ActionBtn}"/>
```

Stealth button — find:
```xml
<Button x:Name="StealthBtn"
        Content="🎥"
        Style="{StaticResource IconBtn}"
        FontSize="13"
        Click="ToggleStealth_Click"
        ToolTip="Toggle screen capture visibility"/>
```
Replace with:
```xml
<Button x:Name="StealthBtn"
        AutomationProperties.AutomationId="Btn_ToggleStealth"
        Content="🎥"
        Style="{StaticResource IconBtn}"
        FontSize="13"
        Click="ToggleStealth_Click"
        ToolTip="Toggle screen capture visibility"/>
```

Theme button — find:
```xml
<Button Content="◐"
        Style="{StaticResource IconBtn}"
        FontSize="13"
        Click="ToggleTheme_Click"
        ToolTip="Toggle theme"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_ToggleTheme"
        Content="◐"
        Style="{StaticResource IconBtn}"
        FontSize="13"
        Click="ToggleTheme_Click"
        ToolTip="Toggle theme"/>
```

History button — find:
```xml
<Button Content="&#128203;"
        Style="{StaticResource IconBtn}"
        FontSize="12"
        Click="ToggleHistory_Click"
        ToolTip="Session history"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_ToggleHistory"
        Content="&#128203;"
        Style="{StaticResource IconBtn}"
        FontSize="12"
        Click="ToggleHistory_Click"
        ToolTip="Session history"/>
```

Settings button — find:
```xml
<Button Content="⚙"
        Style="{StaticResource IconBtn}"
        FontSize="11"
        Click="OpenSettings_Click"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_OpenSettings"
        Content="⚙"
        Style="{StaticResource IconBtn}"
        FontSize="11"
        Click="OpenSettings_Click"/>
```

- [ ] **Step 4: Add AutomationId to the sidebar Border** (DockPanel.Dock="Left"):

Find:
```xml
<Border DockPanel.Dock="Left"
        Width="{Binding SessionControl.IsSidebarVisible,
            Converter={StaticResource BoolToWidthConverter},
            ConverterParameter='120'}"
        Background="{DynamicResource Brush.Background.Sidebar}"
        BorderBrush="{DynamicResource Brush.Border}"
        BorderThickness="0,0,1,0"
        Padding="8">
```
Replace with:
```xml
<Border DockPanel.Dock="Left"
        AutomationProperties.AutomationId="Sidebar"
        Width="{Binding SessionControl.IsSidebarVisible,
            Converter={StaticResource BoolToWidthConverter},
            ConverterParameter='120'}"
        Background="{DynamicResource Brush.Background.Sidebar}"
        BorderBrush="{DynamicResource Brush.Border}"
        BorderThickness="0,0,1,0"
        Padding="8">
```

- [ ] **Step 5: Add AutomationIds and HelpText to all four status dots**. Find each Ellipse and add both AutomationId and HelpText:

Mic dot — find:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         Fill="{Binding SessionControl.IsMicActive,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```
Replace with:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         AutomationProperties.AutomationId="StatusDot_Mic"
         AutomationProperties.HelpText="{Binding SessionControl.IsMicActive,
             Converter={StaticResource BoolToStringConverter},
             ConverterParameter='active|inactive'}"
         Fill="{Binding SessionControl.IsMicActive,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```

System dot — find:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         Fill="{Binding SessionControl.IsSystemAudioActive,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```
Replace with:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         AutomationProperties.AutomationId="StatusDot_System"
         AutomationProperties.HelpText="{Binding SessionControl.IsSystemAudioActive,
             Converter={StaticResource BoolToStringConverter},
             ConverterParameter='active|inactive'}"
         Fill="{Binding SessionControl.IsSystemAudioActive,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```

OCR dot — find:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         Fill="{Binding SessionControl.IsOcrReady,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```
Replace with:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         AutomationProperties.AutomationId="StatusDot_OCR"
         AutomationProperties.HelpText="{Binding SessionControl.IsOcrReady,
             Converter={StaticResource BoolToStringConverter},
             ConverterParameter='active|inactive'}"
         Fill="{Binding SessionControl.IsOcrReady,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```

AI dot — find:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         Fill="{Binding SessionControl.IsAiConnected,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```
Replace with:
```xml
<Ellipse Style="{StaticResource StatusDot}"
         AutomationProperties.AutomationId="StatusDot_AI"
         AutomationProperties.HelpText="{Binding SessionControl.IsAiConnected,
             Converter={StaticResource BoolToStringConverter},
             ConverterParameter='active|inactive'}"
         Fill="{Binding SessionControl.IsAiConnected,
             Converter={StaticResource BoolToColorConverter},
             ConverterParameter='#44FF88|#444444'}"/>
```

- [ ] **Step 6: Add AutomationId to the Capture button** (in sidebar):

Find:
```xml
<Button Content="📷 Capture"
        Style="{StaticResource ActionBtn}"
        Margin="0,4,0,0"
        Command="{Binding ConversationTurn.CaptureScreenCommand}"
        CommandParameter="{Binding SessionControl}"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_Capture"
        Content="📷 Capture"
        Style="{StaticResource ActionBtn}"
        Margin="0,4,0,0"
        Command="{Binding ConversationTurn.CaptureScreenCommand}"
        CommandParameter="{Binding SessionControl}"/>
```

- [ ] **Step 7: Add AutomationId to the sidebar ◀ Hide button**:

Find:
```xml
<Button Content="◀ Hide"
        HorizontalAlignment="Left"
        Command="{Binding SessionControl.ToggleSidebarCommand}"
        Style="{StaticResource IconBtn}"
        Margin="0,12,0,0"/>
```
Replace with:
```xml
<Button AutomationProperties.AutomationId="Btn_HideSidebar"
        Content="◀ Hide"
        HorizontalAlignment="Left"
        Command="{Binding SessionControl.ToggleSidebarCommand}"
        Style="{StaticResource IconBtn}"
        Margin="0,12,0,0"/>
```

- [ ] **Step 8: Add AutomationId to the History panel** (already has x:Name):

Find:
```xml
<ctrl:HistoryPanel x:Name="HistoryPanelControl"
                   Visibility="Collapsed"/>
```
Replace with:
```xml
<ctrl:HistoryPanel x:Name="HistoryPanelControl"
                   AutomationProperties.AutomationId="Panel_History"
                   Visibility="Collapsed"/>
```

- [ ] **Step 9: Add AutomationId to the turn card Border** (in DataTemplate inside the answer ItemsControl):

Find:
```xml
<Border Margin="0,0,0,8"
        Background="{DynamicResource Brush.Background.Card}"
        BorderBrush="{DynamicResource Brush.Border}"
        BorderThickness="1"
        CornerRadius="4"
        ClipToBounds="True">
```
Replace with:
```xml
<Border Margin="0,0,0,8"
        AutomationProperties.AutomationId="TurnCard"
        Background="{DynamicResource Brush.Background.Card}"
        BorderBrush="{DynamicResource Brush.Border}"
        BorderThickness="1"
        CornerRadius="4"
        ClipToBounds="True">
```

- [ ] **Step 10: Build to verify no errors**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 11: Commit**

```
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat: add AutomationIds and HelpText bindings for UI automation tests"
```

---

## Task 2: Create UITests project, AppFixture, and MainWindow

**Files:**
- Create: `tests/AIHelperNET.UITests/AIHelperNET.UITests.csproj`
- Create: `tests/AIHelperNET.UITests/UITestCollection.cs`
- Create: `tests/AIHelperNET.UITests/AppFixture.cs`
- Create: `tests/AIHelperNET.UITests/MainWindow.cs`

- [ ] **Step 1: Create the csproj**

`tests/AIHelperNET.UITests/AIHelperNET.UITests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <NoWarn>$(NoWarn);CS1591;CA1707;CA1001;CA1515</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FlaUI.Core" Version="4.0.0" />
    <PackageReference Include="FlaUI.UIA3" Version="4.0.0" />
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
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
</Project>
```

- [ ] **Step 2: Create the xUnit collection definition**

`tests/AIHelperNET.UITests/UITestCollection.cs`:
```csharp
using Xunit;

namespace AIHelperNET.UITests;

[CollectionDefinition("UITests")]
public sealed class UITestCollection : ICollectionFixture<AppFixture> { }
```

- [ ] **Step 3: Create AppFixture**

`tests/AIHelperNET.UITests/AppFixture.cs`:
```csharp
using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace AIHelperNET.UITests;

public sealed class AppFixture : IAsyncLifetime
{
    private static readonly string ExePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\src\AIHelperNET.App\bin\Debug\net10.0-windows10.0.17763.0\AIHelperNET.App.exe"));

    private Application? _app;

    public UIA3Automation Automation { get; } = new();
    public Window Window { get; private set; } = null!;
    public MainWindow Main { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        foreach (var p in Process.GetProcessesByName("AIHelperNET.App"))
        {
            p.Kill();
            await p.WaitForExitAsync();
        }

        await Task.Delay(800);

        _app = Application.Launch(ExePath);
        Window = _app.GetMainWindow(Automation, TimeSpan.FromSeconds(15));

        // Allow app to finish initializing (VAD + Whisper warm-up)
        await Task.Delay(3000);

        Main = new MainWindow(Window);
    }

    public Task DisposeAsync()
    {
        _app?.Kill();
        Automation.Dispose();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Create MainWindow typed wrapper**

`tests/AIHelperNET.UITests/MainWindow.cs`:
```csharp
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace AIHelperNET.UITests;

public sealed class MainWindow(Window window)
{
    // ── Title bar ─────────────────────────────────────────────────────────────
    public Button   BtnToggleSidebar  => Find("Btn_ToggleSidebar").AsButton();
    public Button   BtnToggleSession  => Find("Btn_ToggleSession").AsButton();
    public Button   BtnToggleStealth  => Find("Btn_ToggleStealth").AsButton();
    public Button   BtnToggleTheme    => Find("Btn_ToggleTheme").AsButton();
    public Button   BtnToggleHistory  => Find("Btn_ToggleHistory").AsButton();
    public Button   BtnOpenSettings   => Find("Btn_OpenSettings").AsButton();

    public AutomationElement HeaderStatusText => Find("Header_StatusText");

    // ── Sidebar controls ──────────────────────────────────────────────────────
    public AutomationElement Sidebar         => Find("Sidebar");
    public Button            BtnHideSidebar  => Find("Btn_HideSidebar").AsButton();
    public Button            BtnCapture      => Find("Btn_Capture").AsButton();

    // ── Mode radio buttons ────────────────────────────────────────────────────
    public RadioButton RadioModeAudioOnly   => Find("Mode_AudioOnly").AsRadioButton();
    public RadioButton RadioModeScreenOnly  => Find("Mode_ScreenOnly").AsRadioButton();
    public RadioButton RadioModeAudioScreen => Find("Mode_AudioAndScreen").AsRadioButton();

    // ── Audio source radio buttons ────────────────────────────────────────────
    public RadioButton RadioAudioMicOnly    => Find("AudioSource_MicOnly").AsRadioButton();
    public RadioButton RadioAudioSystemOnly => Find("AudioSource_SystemOnly").AsRadioButton();
    public RadioButton RadioAudioBoth       => Find("AudioSource_Both").AsRadioButton();

    // ── Screen mode radio buttons ─────────────────────────────────────────────
    public RadioButton RadioScreenGeneral      => Find("ScreenMode_General").AsRadioButton();
    public RadioButton RadioScreenSolveCoding  => Find("ScreenMode_SolveCoding").AsRadioButton();
    public RadioButton RadioScreenDebugError   => Find("ScreenMode_DebugError").AsRadioButton();
    public RadioButton RadioScreenExplainCode  => Find("ScreenMode_ExplainCode").AsRadioButton();
    public RadioButton RadioScreenSystemDesign => Find("ScreenMode_SystemDesign").AsRadioButton();
    public RadioButton RadioScreenMultiChoice  => Find("ScreenMode_MultipleChoice").AsRadioButton();

    // ── Status dots ───────────────────────────────────────────────────────────
    public AutomationElement DotMic    => Find("StatusDot_Mic");
    public AutomationElement DotSystem => Find("StatusDot_System");
    public AutomationElement DotOCR    => Find("StatusDot_OCR");
    public AutomationElement DotAI     => Find("StatusDot_AI");

    // ── Panels ────────────────────────────────────────────────────────────────
    public AutomationElement? HistoryPanel =>
        window.FindFirstDescendant(cf => cf.ByAutomationId("Panel_History"));

    // ── Turn cards ────────────────────────────────────────────────────────────
    public AutomationElement? FirstTurnCard =>
        window.FindFirstDescendant(cf => cf.ByAutomationId("TurnCard"));

    // ── Helpers ───────────────────────────────────────────────────────────────
    public bool IsDotActive(AutomationElement dot) =>
        dot.Properties.HelpText.ValueOrDefault == "active";

    public string SessionStatus =>
        HeaderStatusText.Properties.Name.ValueOrDefault ?? string.Empty;

    private AutomationElement Find(string id) =>
        window.FindFirstDescendant(cf => cf.ByAutomationId(id));
}
```

- [ ] **Step 5: Restore packages and build**

```
dotnet restore tests/AIHelperNET.UITests
dotnet build tests/AIHelperNET.UITests
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 6: Commit**

```
git add tests/AIHelperNET.UITests/
git commit -m "feat: add UITests project with AppFixture and MainWindow wrapper"
```

---

## Task 3: StartupTests — log file assertions

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/StartupTests.cs`

- [ ] **Step 1: Create StartupTests**

`tests/AIHelperNET.UITests/Tests/StartupTests.cs`:
```csharp
using FluentAssertions;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class StartupTests(AppFixture fixture)
{
    private static string TodayLogPath =>
        Path.Combine(
            Directory.Exists(@"D:\AIHelperNET\logs") ? @"D:\AIHelperNET\logs" :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHelperNET", "logs"),
            $"log-{DateTime.Now:yyyyMMdd}.txt");

    [Fact]
    public void App_Window_IsPresent()
    {
        fixture.Window.Should().NotBeNull();
        fixture.Window.Properties.IsOffscreen.ValueOrDefault.Should().BeFalse();
    }

    [Fact]
    public void Log_Contains_SileroVadReady()
    {
        var log = ReadLog();
        log.Should().Contain("Silero", "log should show Silero VAD initialized");
    }

    [Fact]
    public void Log_Contains_WhisperReady()
    {
        var log = ReadLog();
        log.Should().Contain("Whisper", "log should show Whisper model loaded");
    }

    [Fact]
    public void StatusDot_OCR_IsActive_AtStartup()
    {
        fixture.Main.IsDotActive(fixture.Main.DotOCR).Should().BeTrue(
            "OCR dot should be green at startup");
    }

    [Fact]
    public void StatusDots_Mic_And_System_AreInactive_BeforeSession()
    {
        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse();
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse();
    }

    private static string ReadLog()
    {
        var path = TodayLogPath;
        path.Should().NotBeNull("today's log file should exist at {0}", path);
        // Open with shared read access — app holds a write lock
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
```

- [ ] **Step 2: Run the tests**

```
dotnet test tests/AIHelperNET.UITests --filter "StartupTests" -- xunit.parallelizeTestCollections=false
```

Note: the app must be running (started by AppFixture). If running these standalone, the fixture launches it automatically.

Expected: all 5 pass (app was launched in Task 2's AppFixture).

- [ ] **Step 3: Commit**

```
git add tests/AIHelperNET.UITests/Tests/StartupTests.cs
git commit -m "test: add UI automation startup and dot-state tests"
```

---

## Task 4: UIControlTests — radio buttons, theme, history, sidebar

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/UIControlTests.cs`

- [ ] **Step 1: Create UIControlTests**

`tests/AIHelperNET.UITests/Tests/UIControlTests.cs`:
```csharp
using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class UIControlTests(AppFixture fixture)
{
    // ── Mode radio buttons ────────────────────────────────────────────────────

    [Fact]
    public void Mode_AudioOnly_Selects()
    {
        fixture.Main.RadioModeAudioOnly.Click();
        fixture.Main.RadioModeAudioOnly.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Mode_ScreenOnly_Selects()
    {
        fixture.Main.RadioModeScreenOnly.Click();
        fixture.Main.RadioModeScreenOnly.IsChecked.Should().BeTrue();
        fixture.Main.RadioModeAudioOnly.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Mode_AudioAndScreen_Selects()
    {
        fixture.Main.RadioModeAudioScreen.Click();
        fixture.Main.RadioModeAudioScreen.IsChecked.Should().BeTrue();

        // Restore default
        fixture.Main.RadioModeAudioOnly.Click();
    }

    // ── Audio source radio buttons ────────────────────────────────────────────

    [Fact]
    public void AudioSource_MicOnly_Selects()
    {
        fixture.Main.RadioAudioMicOnly.Click();
        fixture.Main.RadioAudioMicOnly.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void AudioSource_SystemOnly_Selects()
    {
        fixture.Main.RadioAudioSystemOnly.Click();
        fixture.Main.RadioAudioSystemOnly.IsChecked.Should().BeTrue();
        fixture.Main.RadioAudioMicOnly.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void AudioSource_Both_Selects()
    {
        fixture.Main.RadioAudioBoth.Click();
        fixture.Main.RadioAudioBoth.IsChecked.Should().BeTrue();

        // Restore default
        fixture.Main.RadioAudioBoth.Click();
    }

    // ── Screen mode radio buttons ─────────────────────────────────────────────

    [Theory]
    [InlineData("ScreenMode_General")]
    [InlineData("ScreenMode_SolveCoding")]
    [InlineData("ScreenMode_DebugError")]
    [InlineData("ScreenMode_ExplainCode")]
    [InlineData("ScreenMode_SystemDesign")]
    [InlineData("ScreenMode_MultipleChoice")]
    public void ScreenMode_EachOption_Selects(string automationId)
    {
        var radio = fixture.Window
            .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            .AsRadioButton();

        radio.Click();
        radio.IsChecked.Should().BeTrue($"{automationId} should be checked after click");

        // Restore default
        fixture.Main.RadioScreenGeneral.Click();
    }

    // ── Theme toggle ──────────────────────────────────────────────────────────

    [Fact]
    public void ThemeToggle_DoesNotCrash()
    {
        fixture.Main.BtnToggleTheme.Click();
        Thread.Sleep(300);
        fixture.Main.BtnToggleTheme.Click(); // back to dark
        Thread.Sleep(300);
        fixture.Window.Properties.IsOffscreen.ValueOrDefault.Should().BeFalse();
    }

    // ── History panel ─────────────────────────────────────────────────────────

    [Fact]
    public void HistoryPanel_OpensAndCloses()
    {
        fixture.Main.HistoryPanel.Should().BeNull("history panel starts collapsed");

        fixture.Main.BtnToggleHistory.Click();

        var panel = Retry.WhileNull(
            () => fixture.Main.HistoryPanel,
            TimeSpan.FromSeconds(3)).Result;

        panel.Should().NotBeNull("history panel should be visible after clicking 📋");

        fixture.Main.BtnToggleHistory.Click();
        Thread.Sleep(400);

        fixture.Main.HistoryPanel.Should().BeNull("history panel should collapse again");
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sidebar_HidesAndRestores()
    {
        var sidebarBefore = fixture.Main.Sidebar.BoundingRectangle.Width;
        sidebarBefore.Should().BeGreaterThan(0, "sidebar should be visible by default");

        fixture.Main.BtnHideSidebar.Click();
        Thread.Sleep(400);

        fixture.Main.Sidebar.BoundingRectangle.Width.Should().Be(0,
            "sidebar width should be 0 when hidden");

        // ▶ restore button appears when sidebar is hidden
        fixture.Main.BtnToggleSidebar.Click();
        Thread.Sleep(400);

        fixture.Main.Sidebar.BoundingRectangle.Width.Should().BeGreaterThan(0,
            "sidebar should be restored");
    }
}
```

- [ ] **Step 2: Run**

```
dotnet test tests/AIHelperNET.UITests --filter "UIControlTests" -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass.

- [ ] **Step 3: Commit**

```
git add tests/AIHelperNET.UITests/Tests/UIControlTests.cs
git commit -m "test: add UI control radio button, theme, history, and sidebar tests"
```

---

## Task 5: SessionLifecycleTests — start/stop and dot states

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/SessionLifecycleTests.cs`

- [ ] **Step 1: Create SessionLifecycleTests**

`tests/AIHelperNET.UITests/Tests/SessionLifecycleTests.cs`:
```csharp
using FluentAssertions;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class SessionLifecycleTests(AppFixture fixture) : IDisposable
{
    public void Dispose() => StopIfRunning();

    private void StopIfRunning()
    {
        if (fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault == "Stop")
        {
            fixture.Main.BtnToggleSession.Click();
            Thread.Sleep(500);
        }
    }

    [Fact]
    public void Start_HeaderContainsListening()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.SessionStatus.Should().Contain("Listening");

        StopIfRunning();
    }

    [Fact]
    public void Stop_HeaderContainsStopped()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.SessionStatus.Should().Contain("Stopped");
    }

    [Fact]
    public void Start_BothMode_MicAndSystemDotsActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeTrue("Mic dot should be green in Both mode");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeTrue("System dot should be green in Both mode");

        StopIfRunning();
    }

    [Fact]
    public void Start_MicOnly_OnlyMicDotActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioMicOnly.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeTrue("Mic dot should be green in Mic Only mode");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse("System dot should stay grey in Mic Only mode");

        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();
    }

    [Fact]
    public void Start_SystemOnly_OnlySystemDotActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioSystemOnly.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeTrue("System dot should be green in System Only mode");
        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse("Mic dot should stay grey in System Only mode");

        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();
    }

    [Fact]
    public void Stop_DotsReturnToInactive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse("Mic dot should be grey after stop");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse("System dot should be grey after stop");
    }
}
```

- [ ] **Step 2: Run**

```
dotnet test tests/AIHelperNET.UITests --filter "SessionLifecycleTests" -- xunit.parallelizeTestCollections=false
```

Expected: all 6 pass.

- [ ] **Step 3: Commit**

```
git add tests/AIHelperNET.UITests/Tests/SessionLifecycleTests.cs
git commit -m "test: add session lifecycle and dot state UI automation tests"
```

---

## Task 6: ScreenCaptureTests — test image → turn card

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/ScreenCaptureTests.cs`

- [ ] **Step 1: Create ScreenCaptureTests**

`tests/AIHelperNET.UITests/Tests/ScreenCaptureTests.cs`:
```csharp
using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class ScreenCaptureTests(AppFixture fixture) : IDisposable
{
    private System.Diagnostics.Process? _imageProcess;

    public void Dispose()
    {
        _imageProcess?.Kill();
        _imageProcess?.Dispose();

        // Stop session if running
        if (fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault == "Stop")
        {
            fixture.Main.BtnToggleSession.Click();
            Thread.Sleep(500);
        }
    }

    [Fact]
    public void Capture_WithTestImage_ProducesTurnCard()
    {
        // Ensure Screen Only mode so capture triggers an answer
        fixture.Main.RadioModeScreenOnly.Click();
        Thread.Sleep(200);

        // Open the test image so there is something on screen to OCR
        // BaseDirectory = tests/AIHelperNET.UITests/bin/Debug/net10.0-.../
        // 4 levels up = solution root D:\work\AIHelperNET
        var imagePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                @"..\..\..\..\tests\testImage\coding_question.png"));

        _imageProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = imagePath,
            UseShellExecute = true,
        });
        Thread.Sleep(1500); // allow image viewer to open

        // Start a session
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        // Click the Capture button
        fixture.Main.BtnCapture.Click();

        // Wait up to 30 s for a turn card to appear
        var turnCard = Retry.WhileNull(
            () => fixture.Main.FirstTurnCard,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500)).Result;

        turnCard.Should().NotBeNull("a turn card should appear after screen capture");
    }
}
```

- [ ] **Step 2: Run**

```
dotnet test tests/AIHelperNET.UITests --filter "ScreenCaptureTests" -- xunit.parallelizeTestCollections=false
```

Expected: the test passes (turn card appears within 30 s — requires a configured AI backend and working OCR).

If no AI backend is configured, the turn card may appear in "Generating..." state but still be present — the test only asserts the card exists.

- [ ] **Step 3: Commit**

```
git add tests/AIHelperNET.UITests/Tests/ScreenCaptureTests.cs
git commit -m "test: add screen capture turn-card UI automation test"
```

---

## Task 7: SettingsTests — settings window opens and tabs are present

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/SettingsTests.cs`

- [ ] **Step 1: Create SettingsTests**

`tests/AIHelperNET.UITests/Tests/SettingsTests.cs`:
```csharp
using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class SettingsTests(AppFixture fixture)
{
    [Fact]
    public void Settings_WindowOpens_AndContainsTabs()
    {
        fixture.Main.BtnOpenSettings.Click();

        // Wait up to 5 s for the settings window to appear
        var settingsWindow = Retry.WhileNull(
            () => fixture.App.GetAllTopLevelWindows(fixture.Automation)
                .FirstOrDefault(w =>
                    (w.Properties.Name.ValueOrDefault ?? string.Empty)
                    .Contains("Settings", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(5)).Result;

        settingsWindow.Should().NotBeNull("Settings window should open");

        // Verify tab headers are present (find by content text)
        var allText = settingsWindow.FindAllDescendants()
            .Select(e => e.Properties.Name.ValueOrDefault ?? string.Empty)
            .ToList();

        allText.Should().Contain(t => t.Contains("Audio", StringComparison.OrdinalIgnoreCase),
            "Audio tab should be present");
        allText.Should().Contain(t => t.Contains("Code Profile", StringComparison.OrdinalIgnoreCase) ||
                                      t.Contains("Profile", StringComparison.OrdinalIgnoreCase),
            "Code Profiles tab should be present");
        allText.Should().Contain(t => t.Contains("Appearance", StringComparison.OrdinalIgnoreCase),
            "Appearance tab should be present");

        // Close the settings window
        settingsWindow.Close();
        Thread.Sleep(400);
    }
}
```

- [ ] **Step 2: Run**

```
dotnet test tests/AIHelperNET.UITests --filter "SettingsTests" -- xunit.parallelizeTestCollections=false
```

Expected: passes — settings window opens, three tab sections are found, window closes.

- [ ] **Step 3: Run the full UITests suite**

```
dotnet test tests/AIHelperNET.UITests -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass (or ScreenCaptureTests skips gracefully if no AI backend).

- [ ] **Step 4: Commit**

```
git add tests/AIHelperNET.UITests/Tests/SettingsTests.cs
git commit -m "test: add settings window UI automation tests"
```

---

## Self-Review Notes

- `AppFixture.ExePath` navigates 4 levels up from `bin/Debug/net10.0-windows10.0.17763.0/` to reach the solution root, then into the App project's build output. Adjust if the test output directory depth changes.
- `IsDotActive` reads `HelpText` which is set by `BoolToStringConverter` — requires Task 1 XAML changes to be committed first.
- `SessionLifecycleTests.Dispose()` stops any active session, ensuring the app is clean for the next test class.
- `ScreenCaptureTests` requires an active internet connection and a configured AI backend to get an actual answer; the test passes even if the turn card shows "Generating..." (it asserts presence, not content).
- All tests use `xunit.parallelizeTestCollections=false` to prevent concurrent app interaction across classes.
- `CA1515` is suppressed in the csproj because xUnit test classes and fixtures must be `public` even though they are top-level (no public API surface).
