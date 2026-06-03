# Visual Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all hardcoded XAML colors and ad-hoc font sizes with a token-driven ResourceDictionary system and add runtime dark/light theme switching via a title-bar toggle button.

**Architecture:** Four new resource files (`DarkTheme.xaml`, `LightTheme.xaml`, `Sizes.xaml`, `Styles.xaml`) are merged in `App.xaml`. All control styles use `DynamicResource` exclusively so WPF re-resolves colors when `ThemeManager` swaps the active theme dictionary at runtime. `MainOverlayWindow` and `SettingsWindow` are updated to reference tokens instead of hardcoded values.

**Tech Stack:** .NET 10, WPF (UseWPF=true), C# 13, XAML ResourceDictionary, no new NuGet packages.

**Spec:** `docs/superpowers/specs/2026-06-03-visual-redesign-design.md`

---

## File Map

| Action | Path | Purpose |
|---|---|---|
| Create | `src/AIHelperNET.App/Resources/DarkTheme.xaml` | Dark navy color tokens |
| Create | `src/AIHelperNET.App/Resources/LightTheme.xaml` | Light slate color tokens |
| Create | `src/AIHelperNET.App/Resources/Sizes.xaml` | Font size tokens |
| Create | `src/AIHelperNET.App/Resources/Styles.xaml` | All control styles (Button, ActionBtn, IconBtn, RadioBtn, GridSplitter, ScrollBar, SectionLabel, StatusDot) |
| Modify | `src/AIHelperNET.App/App.xaml` | Merge the four resource files; remove old ActionBtn style |
| Modify | `src/AIHelperNET.App/App.xaml.cs` | Add `ThemeManager` static class |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` | Replace hardcoded colors/sizes with DynamicResource tokens; add theme toggle button; restructure turn card |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs` | Add `ToggleTheme_Click` handler |
| Modify | `src/AIHelperNET.App/Windows/SettingsWindow.xaml` | Apply token-based dark/light styling |

---

## Task 1: Create token resource dictionaries

**Files:**
- Create: `src/AIHelperNET.App/Resources/DarkTheme.xaml`
- Create: `src/AIHelperNET.App/Resources/LightTheme.xaml`
- Create: `src/AIHelperNET.App/Resources/Sizes.xaml`

- [ ] **Step 1: Create `Resources/` directory and `DarkTheme.xaml`**

`src/AIHelperNET.App/Resources/DarkTheme.xaml` — complete file:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.Background.Window"          Color="#1A1A2E"/>
    <SolidColorBrush x:Key="Brush.Background.Panel"           Color="#0D0D1A"/>
    <SolidColorBrush x:Key="Brush.Background.Sidebar"         Color="#0A0A1A"/>
    <SolidColorBrush x:Key="Brush.Background.Card"            Color="#111827"/>
    <SolidColorBrush x:Key="Brush.Background.TitleBar"        Color="#0D0D1A"/>
    <SolidColorBrush x:Key="Brush.Background.Button"          Color="#2A2A4A"/>
    <SolidColorBrush x:Key="Brush.Background.Button.Hover"    Color="#3A3A6A"/>
    <SolidColorBrush x:Key="Brush.Background.Button.Pressed"  Color="#1E1E38"/>
    <SolidColorBrush x:Key="Brush.Foreground.Primary"         Color="#EAEAEA"/>
    <SolidColorBrush x:Key="Brush.Foreground.Secondary"       Color="#AAAAAA"/>
    <SolidColorBrush x:Key="Brush.Foreground.Muted"           Color="#666666"/>
    <SolidColorBrush x:Key="Brush.Foreground.Button"          Color="#CCCCCC"/>
    <SolidColorBrush x:Key="Brush.Accent"                     Color="#4F8EF7"/>
    <SolidColorBrush x:Key="Brush.Semantic.Active"            Color="#44FF88"/>
    <SolidColorBrush x:Key="Brush.Semantic.Question"          Color="#FFCC44"/>
    <SolidColorBrush x:Key="Brush.Border"                     Color="#2A2A3A"/>
    <SolidColorBrush x:Key="Brush.Splitter"                   Color="#2A2A4A"/>
    <SolidColorBrush x:Key="Brush.Splitter.Hover"             Color="#804F8EF7"/>
</ResourceDictionary>
```

- [ ] **Step 2: Create `LightTheme.xaml`**

`src/AIHelperNET.App/Resources/LightTheme.xaml` — complete file:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Brush.Background.Window"          Color="#F1F5F9"/>
    <SolidColorBrush x:Key="Brush.Background.Panel"           Color="#E8EDF3"/>
    <SolidColorBrush x:Key="Brush.Background.Sidebar"         Color="#DDE3EC"/>
    <SolidColorBrush x:Key="Brush.Background.Card"            Color="#FFFFFF"/>
    <SolidColorBrush x:Key="Brush.Background.TitleBar"        Color="#E2E8F0"/>
    <SolidColorBrush x:Key="Brush.Background.Button"          Color="#CBD5E1"/>
    <SolidColorBrush x:Key="Brush.Background.Button.Hover"    Color="#B8C5D6"/>
    <SolidColorBrush x:Key="Brush.Background.Button.Pressed"  Color="#A0B0C4"/>
    <SolidColorBrush x:Key="Brush.Foreground.Primary"         Color="#1E293B"/>
    <SolidColorBrush x:Key="Brush.Foreground.Secondary"       Color="#475569"/>
    <SolidColorBrush x:Key="Brush.Foreground.Muted"           Color="#94A3B8"/>
    <SolidColorBrush x:Key="Brush.Foreground.Button"          Color="#1E293B"/>
    <SolidColorBrush x:Key="Brush.Accent"                     Color="#4F8EF7"/>
    <SolidColorBrush x:Key="Brush.Semantic.Active"            Color="#16A34A"/>
    <SolidColorBrush x:Key="Brush.Semantic.Question"          Color="#D97706"/>
    <SolidColorBrush x:Key="Brush.Border"                     Color="#CBD5E1"/>
    <SolidColorBrush x:Key="Brush.Splitter"                   Color="#94A3B8"/>
    <SolidColorBrush x:Key="Brush.Splitter.Hover"             Color="#804F8EF7"/>
</ResourceDictionary>
```

- [ ] **Step 3: Create `Sizes.xaml`**

`src/AIHelperNET.App/Resources/Sizes.xaml` — complete file:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:sys="clr-namespace:System;assembly=System.Runtime">
    <!-- Font scale -->
    <sys:Double x:Key="Font.XS">9</sys:Double>
    <sys:Double x:Key="Font.SM">11</sys:Double>
    <sys:Double x:Key="Font.MD">12</sys:Double>
    <!-- Spacing reference values: XS=2, SM=4, MD=8, LG=12, XL=16
         Use these values for Margin/Padding inline in XAML for consistency. -->
</ResourceDictionary>
```

- [ ] **Step 4: Build to verify XAML is well-formed (no merge yet — just confirm files exist)**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded. (The files exist but aren't merged yet — no build impact.)

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.App/Resources/
git commit -m "feat(app): add DarkTheme, LightTheme, Sizes resource dictionaries"
```

---

## Task 2: Create Styles.xaml — Button, ActionBtn, IconBtn, SectionLabel, StatusDot

**Files:**
- Create: `src/AIHelperNET.App/Resources/Styles.xaml`

- [ ] **Step 1: Create `Styles.xaml` with Button, ActionBtn, IconBtn, SectionLabel, StatusDot**

`src/AIHelperNET.App/Resources/Styles.xaml` — write this exact content:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ═══════════════════════════════════════════
         Button — default implicit style with
         CornerRadius=3 and hover/pressed/disabled
    ══════════════════════════════════════════════ -->
    <Style TargetType="Button">
        <Setter Property="Background"      Value="{DynamicResource Brush.Background.Button}"/>
        <Setter Property="Foreground"      Value="{DynamicResource Brush.Foreground.Button}"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Padding"         Value="6,3"/>
        <Setter Property="FontSize"        Value="{DynamicResource Font.SM}"/>
        <Setter Property="Cursor"          Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Button">
                    <Border x:Name="border"
                            Background="{TemplateBinding Background}"
                            BorderBrush="{TemplateBinding BorderBrush}"
                            BorderThickness="{TemplateBinding BorderThickness}"
                            CornerRadius="3"
                            Padding="{TemplateBinding Padding}">
                        <ContentPresenter HorizontalAlignment="Center"
                                          VerticalAlignment="Center"/>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="Background"
                                    Value="{DynamicResource Brush.Background.Button.Hover}"/>
                        </Trigger>
                        <Trigger Property="IsPressed" Value="True">
                            <Setter TargetName="border" Property="Background"
                                    Value="{DynamicResource Brush.Background.Button.Pressed}"/>
                        </Trigger>
                        <Trigger Property="IsEnabled" Value="False">
                            <Setter TargetName="border" Property="Opacity" Value="0.4"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ActionBtn — small pill for Copy/Regen/Dismiss/Resolve -->
    <Style x:Key="ActionBtn" TargetType="Button"
           BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Padding" Value="5,2"/>
        <Setter Property="Margin"  Value="0,0,4,0"/>
    </Style>

    <!-- IconBtn — transparent icon button (title bar, sidebar hide) -->
    <Style x:Key="IconBtn" TargetType="Button"
           BasedOn="{StaticResource {x:Type Button}}">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground.Muted}"/>
        <Setter Property="Padding"    Value="4,2"/>
    </Style>

    <!-- SectionLabel — small-caps heading (TRANSCRIPT, MODE, STATUS …) -->
    <Style x:Key="SectionLabel" TargetType="TextBlock">
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground.Muted}"/>
        <Setter Property="FontSize"   Value="{DynamicResource Font.XS}"/>
        <Setter Property="FontWeight" Value="SemiBold"/>
        <Setter Property="Margin"     Value="0,0,0,4"/>
    </Style>

    <!-- StatusDot — 7×7 indicator ellipse (Fill set per-element via binding) -->
    <Style x:Key="StatusDot" TargetType="Ellipse">
        <Setter Property="Width"  Value="7"/>
        <Setter Property="Height" Value="7"/>
        <Setter Property="Margin" Value="0,0,4,0"/>
    </Style>

</ResourceDictionary>
```

- [ ] **Step 2: Build to confirm no parse errors**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/Resources/Styles.xaml
git commit -m "feat(app): add Styles.xaml with Button, ActionBtn, IconBtn, SectionLabel, StatusDot"
```

---

## Task 3: Extend Styles.xaml — RadioBtn, GridSplitter, ScrollBar, ScrollViewer

**Files:**
- Modify: `src/AIHelperNET.App/Resources/Styles.xaml`

- [ ] **Step 1: Append RadioBtn, GridSplitter, ScrollBar, ScrollViewer styles**

Add the following immediately before the closing `</ResourceDictionary>` tag in `Styles.xaml`:

```xml
    <!-- RadioBtn — custom circle indicator replacing WPF default -->
    <Style x:Key="RadioBtn" TargetType="RadioButton">
        <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground.Secondary}"/>
        <Setter Property="FontSize"   Value="{DynamicResource Font.SM}"/>
        <Setter Property="Margin"     Value="0,2"/>
        <Setter Property="Cursor"     Value="Hand"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="RadioButton">
                    <StackPanel Orientation="Horizontal" VerticalAlignment="Center">
                        <Grid Width="12" Height="12" Margin="0,0,5,0"
                              VerticalAlignment="Center">
                            <Ellipse x:Name="outer"
                                     Fill="Transparent"
                                     Stroke="{DynamicResource Brush.Foreground.Muted}"
                                     StrokeThickness="1.5"/>
                            <Ellipse x:Name="inner"
                                     Width="4" Height="4"
                                     Fill="White"
                                     Visibility="Collapsed"/>
                        </Grid>
                        <ContentPresenter VerticalAlignment="Center"/>
                    </StackPanel>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsChecked" Value="True">
                            <Setter TargetName="outer" Property="Fill"
                                    Value="{DynamicResource Brush.Accent}"/>
                            <Setter TargetName="outer" Property="Stroke"
                                    Value="{DynamicResource Brush.Accent}"/>
                            <Setter TargetName="inner" Property="Visibility"
                                    Value="Visible"/>
                            <Setter Property="Foreground"
                                    Value="{DynamicResource Brush.Foreground.Primary}"/>
                        </Trigger>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter Property="Foreground"
                                    Value="{DynamicResource Brush.Foreground.Primary}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- GridSplitter — hover changes to accent tint -->
    <Style TargetType="GridSplitter">
        <Setter Property="Background" Value="{DynamicResource Brush.Splitter}"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="GridSplitter">
                    <Border x:Name="border" Background="{TemplateBinding Background}"/>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="border" Property="Background"
                                    Value="{DynamicResource Brush.Splitter.Hover}"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- Thumb used by the slim ScrollBar below -->
    <Style x:Key="SlimScrollBarThumb" TargetType="Thumb">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="Thumb">
                    <Border x:Name="thumb"
                            CornerRadius="3"
                            Background="{DynamicResource Brush.Foreground.Muted}"
                            Opacity="0.5"/>
                    <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                            <Setter TargetName="thumb" Property="Opacity" Value="0.8"/>
                        </Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>

    <!-- ScrollBar — 6px slim, no arrow buttons -->
    <Style TargetType="ScrollBar">
        <Setter Property="OverridesDefaultStyle" Value="True"/>
        <Setter Property="Width"    Value="6"/>
        <Setter Property="MinWidth" Value="6"/>
        <Setter Property="Template">
            <Setter.Value>
                <ControlTemplate TargetType="ScrollBar">
                    <Grid>
                        <Track x:Name="PART_Track" IsDirectionReversed="True">
                            <Track.Thumb>
                                <Thumb Style="{StaticResource SlimScrollBarThumb}"
                                       Margin="0,2"/>
                            </Track.Thumb>
                        </Track>
                    </Grid>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
        <Style.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
                <Setter Property="Width"     Value="Auto"/>
                <Setter Property="MinWidth"  Value="0"/>
                <Setter Property="Height"    Value="6"/>
                <Setter Property="MinHeight" Value="6"/>
            </Trigger>
        </Style.Triggers>
    </Style>

    <!-- ScrollViewer — auto vertical, disabled horizontal (default for all) -->
    <Style TargetType="ScrollViewer">
        <Setter Property="VerticalScrollBarVisibility"   Value="Auto"/>
        <Setter Property="HorizontalScrollBarVisibility" Value="Disabled"/>
    </Style>
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/Resources/Styles.xaml
git commit -m "feat(app): add RadioBtn, GridSplitter, ScrollBar styles to Styles.xaml"
```

---

## Task 4: Update App.xaml — merge resource dictionaries

**Files:**
- Modify: `src/AIHelperNET.App/App.xaml`

- [ ] **Step 1: Replace `App.xaml` entirely**

`src/AIHelperNET.App/App.xaml` — complete file:

```xml
<Application x:Class="AIHelperNET.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:local="clr-namespace:AIHelperNET.App">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <ResourceDictionary Source="Resources/DarkTheme.xaml"/>
                <ResourceDictionary Source="Resources/Sizes.xaml"/>
                <ResourceDictionary Source="Resources/Styles.xaml"/>
            </ResourceDictionary.MergedDictionaries>
            <local:BoolToVisibilityInverseConverter x:Key="InverseBoolToVisibilityConverter"/>
            <local:BoolToColorConverter             x:Key="BoolToColorConverter"/>
            <local:BoolToStringConverter            x:Key="BoolToStringConverter"/>
            <local:BoolToWidthConverter             x:Key="BoolToWidthConverter"/>
            <local:EnumToBoolConverter              x:Key="EnumToBoolConverter"/>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

Note: The old inline `ActionBtn` style is removed — it now lives in `Styles.xaml`.

- [ ] **Step 2: Build — this is the first real integration check**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded. If you see `XamlParseException` or `Resource not found`, check that the three `Source=` paths match the actual file names exactly (case-sensitive on some systems).

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/App.xaml
git commit -m "feat(app): wire App.xaml to merge DarkTheme, Sizes, Styles dictionaries"
```

---

## Task 5: Add ThemeManager and ToggleTheme_Click

**Files:**
- Modify: `src/AIHelperNET.App/App.xaml.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

- [ ] **Step 1: Add `ThemeManager` to `App.xaml.cs`**

Add the following class after the closing brace of the `App` class, still inside the `AIHelperNET.App` namespace:

```csharp
static class ThemeManager
{
    const string DarkUri  = "Resources/DarkTheme.xaml";
    const string LightUri = "Resources/LightTheme.xaml";

    static bool _isDark = true;

    public static void Toggle()
    {
        var dicts   = Application.Current.Resources.MergedDictionaries;
        var current = dicts.First(d => d.Source?.OriginalString.Contains("Theme") == true);
        dicts.Remove(current);
        _isDark = !_isDark;
        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri(_isDark ? DarkUri : LightUri, UriKind.Relative)
        });
    }
}
```

The full file after the edit (`App.xaml.cs`):

```csharp
using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AIHelperNET.App;

/// <summary>Application entry point and host lifecycle coordinator.</summary>
public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureAIHelper();
        _host = builder.Build();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();

        var overlay = _host.Services.GetRequiredService<MainOverlayWindow>();

        var transcriptSink = _host.Services.GetRequiredService<TranscriptSink>();
        var transcriptVm   = _host.Services.GetRequiredService<TranscriptViewModel>();
        transcriptSink.SetHandler(item => transcriptVm.AddItem(item));

        var answerSink = _host.Services.GetRequiredService<AnswerStreamSink>();
        var turnVm     = _host.Services.GetRequiredService<ConversationTurnViewModel>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => ConversationTurnViewModel.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));

        overlay.Show();
        WireHotkeys(overlay);
    }

    private void WireHotkeys(MainOverlayWindow overlay)
    {
        var hotkeys = _host.Services.GetRequiredService<IGlobalHotkeyService>() as GlobalHotkeyService;
        if (hotkeys is null) return;

        var hwnd = new WindowInteropHelper(overlay).Handle;
        hotkeys.Initialize(hwnd);

        hotkeys.Register(HotkeyId.ToggleSession,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Space);
        hotkeys.Register(HotkeyId.CaptureScreen,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.S);
        hotkeys.Register(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q);
        hotkeys.Register(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.C);
        hotkeys.Register(HotkeyId.ToggleOverlay,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.H);

        var sessionVm = _host.Services.GetRequiredService<SessionControlViewModel>();
        var turnVm2   = _host.Services.GetRequiredService<ConversationTurnViewModel>();

        hotkeys.HotkeyPressed += (_, id) =>
        {
            switch (id)
            {
                case HotkeyId.ToggleSession:
                    _ = sessionVm.ToggleSessionCommand.ExecuteAsync(null);
                    break;
                case HotkeyId.CaptureScreen:
                    _ = turnVm2.CaptureScreenCommand.ExecuteAsync(null);
                    break;
                case HotkeyId.GenerateAnswer:
                    _ = turnVm2.RegenerateCommand.ExecuteAsync(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.CopyAnswer:
                    turnVm2.CopyLatestCommand.Execute(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.ToggleOverlay:
                    sessionVm.ToggleSidebarCommand.Execute(null);
                    break;
            }
        };
    }

    /// <inheritdoc/>
    protected override async void OnExit(ExitEventArgs e)
    {
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}

static class ThemeManager
{
    const string DarkUri  = "Resources/DarkTheme.xaml";
    const string LightUri = "Resources/LightTheme.xaml";

    static bool _isDark = true;

    public static void Toggle()
    {
        var dicts   = Application.Current.Resources.MergedDictionaries;
        var current = dicts.First(d => d.Source?.OriginalString.Contains("Theme") == true);
        dicts.Remove(current);
        _isDark = !_isDark;
        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri(_isDark ? DarkUri : LightUri, UriKind.Relative)
        });
    }
}
```

- [ ] **Step 2: Add `ToggleTheme_Click` to `MainOverlayWindow.xaml.cs`**

Add the handler after `OpenSettings_Click`. The full file after the edit:

```csharp
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;
using Serilog;

namespace AIHelperNET.App.Windows;

/// <summary>Composite data context for <see cref="MainOverlayWindow"/>.</summary>
public sealed class MainOverlayWindowContext(
    SessionControlViewModel sessionControl,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn)
{
    /// <summary>Gets the session control view model.</summary>
    public SessionControlViewModel SessionControl    => sessionControl;

    /// <summary>Gets the transcript view model.</summary>
    public TranscriptViewModel Transcript            => transcript;

    /// <summary>Gets the conversation turn view model.</summary>
    public ConversationTurnViewModel ConversationTurn => conversationTurn;
}

/// <summary>The stealth overlay window excluded from screen capture.</summary>
[SupportedOSPlatform("windows")]
public partial class MainOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_MONITOR            = 0x00000001;

    private readonly SettingsWindow _settingsWindow;

    /// <summary>Initialises a new instance of <see cref="MainOverlayWindow"/>.</summary>
    public MainOverlayWindow(MainOverlayWindowContext context, SettingsWindow settingsWindow)
    {
        InitializeComponent();
        DataContext     = context;
        _settingsWindow = settingsWindow;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            if (!SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                Log.Warning("SetWindowDisplayAffinity failed — overlay may be visible to screen capture");
            else
                Log.Information("Overlay: WDA_MONITOR applied");
        }
        else
        {
            Log.Information("Overlay: WDA_EXCLUDEFROMCAPTURE applied");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        => ThemeManager.Toggle();
}
```

- [ ] **Step 3: Build**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.App/App.xaml.cs src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs
git commit -m "feat(app): add ThemeManager and ToggleTheme_Click handler"
```

---

## Task 6: Update MainOverlayWindow.xaml

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

- [ ] **Step 1: Replace `MainOverlayWindow.xaml` entirely**

`src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` — complete file:

```xml
<Window x:Class="AIHelperNET.App.Windows.MainOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AIHelper"
        Width="600" Height="500"
        MinWidth="320" MinHeight="260"
        WindowStyle="None"
        Background="{DynamicResource Brush.Background.Window}"
        Opacity="0.75"
        Topmost="True"
        ResizeMode="CanResizeWithGrip"
        ShowInTaskbar="False">

    <DockPanel>
        <!-- Title bar -->
        <Border DockPanel.Dock="Top"
                Background="{DynamicResource Brush.Background.TitleBar}"
                Padding="8,4"
                MouseLeftButtonDown="TitleBar_MouseLeftButtonDown">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <Button Grid.Column="0" Content="▶"
                        Style="{StaticResource IconBtn}"
                        FontSize="9"
                        Visibility="{Binding SessionControl.IsSidebarVisible,
                            Converter={StaticResource InverseBoolToVisibilityConverter}}"
                        Command="{Binding SessionControl.ToggleSidebarCommand}"
                        Margin="0,0,6,0"/>

                <TextBlock Grid.Column="1"
                           Foreground="{DynamicResource Brush.Foreground.Secondary}"
                           FontSize="{DynamicResource Font.SM}"
                           VerticalAlignment="Center">
                    <Run Text="AIHelper · "/>
                    <Run Text="●"
                         Foreground="{Binding SessionControl.IsSessionActive,
                             Converter={StaticResource BoolToColorConverter},
                             ConverterParameter='#44FF88|#666666'}"/>
                    <Run Text=" "/>
                    <Run Text="{Binding SessionControl.IsSessionActive,
                        Converter={StaticResource BoolToStringConverter},
                        ConverterParameter='Listening|Stopped'}"/>
                </TextBlock>

                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                    <Button Content="{Binding SessionControl.IsSessionActive,
                                Converter={StaticResource BoolToStringConverter},
                                ConverterParameter='Stop|Start'}"
                            Command="{Binding SessionControl.ToggleSessionCommand}"
                            Style="{StaticResource ActionBtn}"/>
                    <Button Content="◐"
                            Style="{StaticResource IconBtn}"
                            FontSize="13"
                            Click="ToggleTheme_Click"
                            ToolTip="Toggle theme"/>
                    <Button Content="⚙"
                            Style="{StaticResource IconBtn}"
                            FontSize="11"
                            Click="OpenSettings_Click"/>
                </StackPanel>
            </Grid>
        </Border>

        <!-- Sidebar -->
        <Border DockPanel.Dock="Left"
                Width="{Binding SessionControl.IsSidebarVisible,
                    Converter={StaticResource BoolToWidthConverter},
                    ConverterParameter='120'}"
                Background="{DynamicResource Brush.Background.Sidebar}"
                BorderBrush="{DynamicResource Brush.Border}"
                BorderThickness="0,0,1,0"
                Padding="8">
            <StackPanel>
                <TextBlock Style="{StaticResource SectionLabel}" Text="MODE" Margin="0,0,0,4"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Audio Only"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=AudioOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Screen Only"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=ScreenOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Audio + Screen"
                             IsChecked="{Binding SessionControl.Mode,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=AudioAndScreen}"/>

                <TextBlock Style="{StaticResource SectionLabel}" Text="AUDIO SOURCE"
                           Margin="0,10,0,4"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Mic Only"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=MicrophoneOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="System Only"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=SystemAudioOnly}"/>
                <RadioButton Style="{StaticResource RadioBtn}" Content="Both"
                             IsChecked="{Binding SessionControl.AudioSource,
                                 Converter={StaticResource EnumToBoolConverter},
                                 ConverterParameter=Both}"/>

                <TextBlock Style="{StaticResource SectionLabel}" Text="STATUS"
                           Margin="0,10,0,4"/>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsMicActive,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="Mic"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                               FontSize="{DynamicResource Font.XS}"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsSystemAudioActive,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="System"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                               FontSize="{DynamicResource Font.XS}"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsOcrReady,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="OCR"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                               FontSize="{DynamicResource Font.XS}"/>
                </StackPanel>
                <StackPanel Orientation="Horizontal" Margin="0,2">
                    <Ellipse Style="{StaticResource StatusDot}"
                             Fill="{Binding SessionControl.IsAiConnected,
                                 Converter={StaticResource BoolToColorConverter},
                                 ConverterParameter='#44FF88|#444444'}"/>
                    <TextBlock Text="AI"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                               FontSize="{DynamicResource Font.XS}"/>
                </StackPanel>

                <Button Content="◀ Hide"
                        HorizontalAlignment="Left"
                        Command="{Binding SessionControl.ToggleSidebarCommand}"
                        Style="{StaticResource IconBtn}"
                        Margin="0,12,0,0"/>
            </StackPanel>
        </Border>

        <!-- Main content: transcript + splitter + answer panels -->
        <Grid>
            <Grid.RowDefinitions>
                <RowDefinition Height="*" MinHeight="60"/>
                <RowDefinition Height="4"/>
                <RowDefinition Height="*" MinHeight="80"/>
            </Grid.RowDefinitions>

            <!-- Transcript panel -->
            <Border Grid.Row="0" Background="{DynamicResource Brush.Background.Panel}">
                <DockPanel>
                    <TextBlock DockPanel.Dock="Top"
                               Style="{StaticResource SectionLabel}"
                               Text="TRANSCRIPT"
                               Margin="8,6,8,3"/>
                    <ScrollViewer x:Name="TranscriptScroll"
                                  VerticalScrollBarVisibility="Auto"
                                  HorizontalScrollBarVisibility="Disabled"
                                  Margin="8,0,8,6">
                        <ItemsControl ItemsSource="{Binding Transcript.Items}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <StackPanel Orientation="Horizontal" Margin="0,2">
                                        <TextBlock Text="{Binding TimestampLabel}"
                                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                                   FontSize="{DynamicResource Font.XS}"
                                                   Margin="0,0,6,0"/>
                                        <TextBlock Text="{Binding SpeakerLabel}"
                                                   Foreground="{Binding SpeakerColor}"
                                                   FontSize="{DynamicResource Font.SM}"
                                                   FontWeight="SemiBold"
                                                   Margin="0,0,4,0"/>
                                        <TextBlock Text="{Binding Text}"
                                                   Foreground="{DynamicResource Brush.Foreground.Secondary}"
                                                   FontSize="{DynamicResource Font.SM}"
                                                   TextWrapping="Wrap"/>
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </DockPanel>
            </Border>

            <!-- Splitter -->
            <GridSplitter Grid.Row="1" HorizontalAlignment="Stretch" Height="4"/>

            <!-- Answer/turn panel -->
            <Border Grid.Row="2" Background="{DynamicResource Brush.Background.Panel}">
                <ScrollViewer Margin="8">
                    <ItemsControl ItemsSource="{Binding ConversationTurn.Turns}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Border Margin="0,0,0,8"
                                        Background="{DynamicResource Brush.Background.Card}"
                                        BorderBrush="{DynamicResource Brush.Border}"
                                        BorderThickness="1"
                                        CornerRadius="4"
                                        ClipToBounds="True">
                                    <DockPanel>
                                        <Rectangle DockPanel.Dock="Left"
                                                   Width="3"
                                                   Fill="{DynamicResource Brush.Accent}"/>
                                        <StackPanel Margin="6">
                                            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                                                <TextBlock Text="❓ "
                                                           Foreground="{DynamicResource Brush.Semantic.Question}"
                                                           FontSize="{DynamicResource Font.XS}"/>
                                                <TextBlock Text="{Binding InitialQuestion}"
                                                           Foreground="{DynamicResource Brush.Semantic.Question}"
                                                           FontSize="{DynamicResource Font.SM}"
                                                           FontWeight="SemiBold"
                                                           TextWrapping="Wrap"/>
                                            </StackPanel>
                                            <TextBlock Text="{Binding StatusLabel}"
                                                       Foreground="{DynamicResource Brush.Foreground.Muted}"
                                                       FontSize="{DynamicResource Font.XS}"
                                                       Margin="0,0,0,4"/>
                                            <TextBlock Text="{Binding LatestVersion.Text}"
                                                       Foreground="{DynamicResource Brush.Foreground.Primary}"
                                                       FontSize="{DynamicResource Font.MD}"
                                                       TextWrapping="Wrap"
                                                       FontFamily="Cascadia Mono, Consolas"
                                                       Margin="0,0,0,4"/>
                                            <StackPanel Orientation="Horizontal">
                                                <Button Content="Copy"
                                                        Command="{Binding DataContext.ConversationTurn.CopyLatestCommand,
                                                            RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}"
                                                        Style="{StaticResource ActionBtn}"/>
                                                <Button Content="Regen"
                                                        Command="{Binding DataContext.ConversationTurn.RegenerateCommand,
                                                            RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}"
                                                        Style="{StaticResource ActionBtn}"/>
                                                <Button Content="Dismiss"
                                                        Command="{Binding DataContext.ConversationTurn.DismissCommand,
                                                            RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}"
                                                        Style="{StaticResource ActionBtn}"/>
                                                <Button Content="Resolve"
                                                        Command="{Binding DataContext.ConversationTurn.ResolveCommand,
                                                            RelativeSource={RelativeSource AncestorType=Window}}"
                                                        CommandParameter="{Binding}"
                                                        Style="{StaticResource ActionBtn}"/>
                                            </StackPanel>
                                        </StackPanel>
                                    </DockPanel>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </ScrollViewer>
            </Border>
        </Grid>
    </DockPanel>
</Window>
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded. If you see `StaticResource not found` for `IconBtn`, `SectionLabel`, `RadioBtn`, or `StatusDot`, confirm `Styles.xaml` is merged in `App.xaml` and the key spellings match exactly.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat(app): update MainOverlayWindow to token-based styles, add theme toggle button"
```

---

## Task 7: Update SettingsWindow.xaml

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

- [ ] **Step 1: Replace `SettingsWindow.xaml` entirely**

`src/AIHelperNET.App/Windows/SettingsWindow.xaml` — complete file:

```xml
<Window x:Class="AIHelperNET.App.Windows.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AIHelper — Settings" Width="480" Height="320"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        Background="{DynamicResource Brush.Background.Window}">
    <StackPanel Margin="20">
        <TextBlock Text="Claude API Key"
                   FontWeight="SemiBold"
                   FontSize="{DynamicResource Font.SM}"
                   Foreground="{DynamicResource Brush.Foreground.Primary}"
                   Margin="0,0,0,6"/>
        <DockPanel>
            <Button Content="Save" DockPanel.Dock="Right" Width="60" Margin="8,0,0,0"
                    Command="{Binding SaveApiKeyCommand}"/>
            <Border Background="{DynamicResource Brush.Background.Panel}"
                    BorderBrush="{DynamicResource Brush.Border}"
                    BorderThickness="1" CornerRadius="3" Padding="1">
                <PasswordBox x:Name="ApiKeyBox"
                             Background="Transparent"
                             Foreground="{DynamicResource Brush.Foreground.Primary}"
                             CaretBrush="{DynamicResource Brush.Foreground.Primary}"
                             BorderThickness="0"
                             Padding="5,3"
                             PasswordChanged="ApiKeyBox_PasswordChanged"/>
            </Border>
        </DockPanel>
        <TextBlock Text="{Binding StatusMessage}"
                   Foreground="{DynamicResource Brush.Semantic.Active}"
                   Margin="0,8,0,0"
                   FontSize="{DynamicResource Font.SM}"/>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Build**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug
```
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
git commit -m "feat(app): apply token-based dark/light styling to SettingsWindow"
```

---

## Task 8: Visual verification

**Files:** None — run and observe.

- [ ] **Step 1: Launch the app**

```
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj
```

- [ ] **Step 2: Dark theme checks (default on launch)**
  - [ ] Window background is dark navy (`#1A1A2E`) — no white flicker
  - [ ] Title bar is slightly darker (`#0D0D1A`)
  - [ ] Sidebar has a visible right border separating it from the transcript panel
  - [ ] Mode/Audio Source radio buttons show blue filled circle when selected, grey ring when not
  - [ ] `Stop`/`Start` button shows hover highlight (lighter blue-purple) on mouse-over
  - [ ] `Copy`, `Regen`, `Dismiss`, `Resolve` buttons show hover highlight on mouse-over
  - [ ] GridSplitter turns blue-tinted on mouse-over
  - [ ] ScrollBar thumb appears as a slim rounded bar (no wide system scrollbar)
  - [ ] Conversation turn cards have a 3px blue left accent strip and a 1px border

- [ ] **Step 3: Toggle to light theme**
  - Click the `◐` button in the title bar
  - [ ] Window shifts to light slate (`#F1F5F9`)
  - [ ] Sidebar shifts to `#DDE3EC`
  - [ ] Turn cards become white with a visible border
  - [ ] Question text shifts from yellow to amber (`#D97706`)
  - [ ] Active status dots shift from bright green to dark green (`#16A34A`)
  - [ ] No residual dark-theme colors visible anywhere

- [ ] **Step 4: Toggle back and open Settings**
  - Click `◐` again — verify switch back to dark
  - Click `⚙` — verify SettingsWindow matches the active theme (dark background, styled PasswordBox, no bare white dialog)

- [ ] **Step 5: Push to origin**

```
git push origin develop
```
