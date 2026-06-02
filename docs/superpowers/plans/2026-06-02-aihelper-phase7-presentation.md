# AIHelperNET — Phase 7: Presentation Layer (WPF)

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` or `superpowers:executing-plans`.
> **Prerequisite:** Phase 6 complete (Infrastructure fully wired).

**Goal:** Build the WPF presentation layer — IHost startup, stealth overlay window, OverlayViewModel with live streaming, SettingsWindow, hotkey→command wiring, AnswerStreamSink, and appsettings.json.

**Architecture:** `IHost` in `App.xaml.cs`. ViewModels depend only on `IMediator`. `IAnswerStreamSink` is implemented here (dispatches chunks to UI thread). Stealth overlay uses `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`.

**Tech Stack:** WPF, CommunityToolkit.Mvvm 8.x, Serilog, IHost (Microsoft.Extensions.Hosting)

---

### Task 37: appsettings.json

**Files:**
- Create: `src/AIHelperNET.App/appsettings.json`

- [ ] **Step 1: Create appsettings.json**

```json
{
  "Claude":  { "BaseUrl": "https://api.anthropic.com", "Model": "claude-sonnet-4-6", "Version": "2023-06-01" },
  "Ollama":  { "BaseUrl": "http://localhost:11434", "Model": "qwen2.5-coder:7b" },
  "Audio":   { "DefaultModel": "Base", "SampleRate": 16000 },
  "Backend": { "Active": "Claude" },
  "Serilog": {
    "MinimumLevel": "Information"
  }
}
```

- [ ] **Step 2: Mark as content file in App csproj**

Edit `src/AIHelperNET.App/AIHelperNET.App.csproj` — add:

```xml
<ItemGroup>
  <Content Include="appsettings.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.App/appsettings.json src/AIHelperNET.App/AIHelperNET.App.csproj
git commit -m "feat(app): add appsettings.json"
```

---

### Task 38: AnswerStreamSink

**Files:**
- Create: `src/AIHelperNET.App/Streaming/AnswerStreamSink.cs`

This is the implementation of `IAnswerStreamSink` declared in Application. It holds a reference to the active `OverlayViewModel` and marshals chunks to the UI thread.

- [ ] **Step 1: Implement**

```csharp
// src/AIHelperNET.App/Streaming/AnswerStreamSink.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.App.Streaming;

public sealed class AnswerStreamSink : IAnswerStreamSink
{
    private Action<AnswerId, string>? _handler;

    public void SetHandler(Action<AnswerId, string> handler)
        => _handler = handler;

    public ValueTask PushAsync(AnswerId answerId, string chunk, CancellationToken ct)
    {
        if (_handler is not null)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _handler(answerId, chunk));
        }
        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.App/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.App/Streaming/AnswerStreamSink.cs
git commit -m "feat(app): add AnswerStreamSink (dispatches chunks to UI thread)"
```

---

### Task 39: OverlayViewModel

**Files:**
- Create: `src/AIHelperNET.App/ViewModels/OverlayViewModel.cs`

- [ ] **Step 1: Implement**

```csharp
// src/AIHelperNET.App/ViewModels/OverlayViewModel.cs
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.App.Streaming;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly AnswerStreamSink _streamSink;

    [ObservableProperty] private string _currentAnswer = string.Empty;
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string _latestQuestion = string.Empty;
    [ObservableProperty] private bool _isVisible = true;

    private SessionId? _sessionId;
    private QuestionId? _latestQuestionId;
    private CancellationTokenSource? _answerCts;
    private string? _lastScreenContext;

    public OverlayViewModel(IMediator mediator, AnswerStreamSink streamSink)
    {
        _mediator = mediator;
        _streamSink = streamSink;
        _streamSink.SetHandler(OnChunk);
    }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsSessionActive)
        {
            var settingsResult = await _mediator.Send(new GetSettingsQuery());
            var settings = settingsResult.IsSuccess ? settingsResult.Value : null;

            var result = await _mediator.Send(new StartSessionCommand(
                settings?.AnswerSettings ?? AnswerSettings.Default,
                settings?.CodeProfile   ?? CodeProfile.Empty));

            if (result.IsSuccess)
            {
                _sessionId = result.Value.Id;
                IsSessionActive = true;
                CurrentAnswer = string.Empty;
                LatestQuestion = string.Empty;
            }
        }
        else if (_sessionId is { } id)
        {
            _answerCts?.Cancel();
            await _mediator.Send(new StopSessionCommand(id));
            IsSessionActive = false;
            _sessionId = null;
        }
    }

    [RelayCommand]
    private async Task GenerateAnswerAsync()
    {
        if (_sessionId is not { } id || _latestQuestionId is not { } qid) return;

        _answerCts?.Cancel();
        _answerCts = new CancellationTokenSource();
        CurrentAnswer = string.Empty;

        await _mediator.Send(
            new GenerateAnswerCommand(id, qid, _lastScreenContext),
            _answerCts.Token);
    }

    [RelayCommand]
    private async Task CaptureScreenAsync()
    {
        var result = await _mediator.Send(new CaptureScreenCommand());
        if (result.IsSuccess) _lastScreenContext = result.Value;
    }

    [RelayCommand]
    private void CopyAnswer()
    {
        if (!string.IsNullOrEmpty(CurrentAnswer))
            System.Windows.Clipboard.SetText(CurrentAnswer);
    }

    [RelayCommand]
    private void ToggleVisibility() => IsVisible = !IsVisible;

    // Called when a new TranscriptSegment is detected as a question externally
    public void OnQuestionDetected(string questionText, QuestionId questionId)
    {
        LatestQuestion = questionText;
        _latestQuestionId = questionId;
    }

    private void OnChunk(AnswerId answerId, string chunk)
    {
        CurrentAnswer += chunk;
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build src/AIHelperNET.App/
```

- [ ] **Step 3: Commit**

```powershell
git add src/AIHelperNET.App/ViewModels/OverlayViewModel.cs
git commit -m "feat(app): add OverlayViewModel with session/answer/screen commands"
```

---

### Task 40: OverlayWindow XAML + code-behind

**Files:**
- Create: `src/AIHelperNET.App/Windows/OverlayWindow.xaml`
- Create: `src/AIHelperNET.App/Windows/OverlayWindow.xaml.cs`

- [ ] **Step 1: Create XAML**

```xml
<!-- src/AIHelperNET.App/Windows/OverlayWindow.xaml -->
<Window x:Class="AIHelperNET.App.Windows.OverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AIHelper"
        Width="500" Height="400"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="#CC1A1A2E"
        Topmost="True"
        ResizeMode="CanResizeWithGrip"
        ShowInTaskbar="False">
    <Window.Style>
        <Style TargetType="Window">
            <Style.Triggers>
                <DataTrigger Binding="{Binding IsVisible}" Value="False">
                    <Setter Property="Visibility" Value="Hidden"/>
                </DataTrigger>
            </Style.Triggers>
        </Style>
    </Window.Style>

    <Grid Margin="12">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Drag handle -->
        <Border Grid.Row="0" Background="#33FFFFFF" CornerRadius="4" Padding="8,4"
                MouseLeftButtonDown="DragHandle_MouseLeftButtonDown" Cursor="SizeAll">
            <TextBlock Text="AIHelper ·  Ctrl+Shift+H to hide"
                       Foreground="#AAD4D4D4" FontSize="10"/>
        </Border>

        <!-- Latest question -->
        <TextBlock Grid.Row="1" Margin="0,8,0,4"
                   Text="{Binding LatestQuestion}"
                   Foreground="#FFFFCC44" FontSize="12" FontWeight="SemiBold"
                   TextWrapping="Wrap"/>

        <!-- Streaming answer -->
        <ScrollViewer Grid.Row="2" VerticalScrollBarVisibility="Auto">
            <TextBlock Text="{Binding CurrentAnswer}"
                       Foreground="#FFEEEEEE" FontSize="13"
                       TextWrapping="Wrap" FontFamily="Cascadia Mono, Consolas, Monospace"/>
        </ScrollViewer>

        <!-- Status bar -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,8,0,0">
            <Ellipse Width="8" Height="8" Margin="0,0,6,0">
                <Ellipse.Style>
                    <Style TargetType="Ellipse">
                        <Setter Property="Fill" Value="#FF666666"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsSessionActive}" Value="True">
                                <Setter Property="Fill" Value="#FF44FF88"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </Ellipse.Style>
            </Ellipse>
            <TextBlock Foreground="#88D4D4D4" FontSize="10">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Setter Property="Text" Value="Session stopped  ·  Ctrl+Shift+Space to start"/>
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding IsSessionActive}" Value="True">
                                <Setter Property="Text" Value="Listening  ·  Ctrl+Shift+Q for answer  ·  Ctrl+Shift+S screen"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>
        </StackPanel>
    </Grid>
</Window>
```

- [ ] **Step 2: Create code-behind**

```csharp
// src/AIHelperNET.App/Windows/OverlayWindow.xaml.cs
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;
using Serilog;

namespace AIHelperNET.App.Windows;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_MONITOR            = 0x00000001;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            // Fallback: WDA_MONITOR renders the window black in captures (older Windows builds)
            if (!SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                Log.Warning("SetWindowDisplayAffinity failed — overlay may be visible to screen capture");
            else
                Log.Information("Overlay: WDA_MONITOR applied (WDA_EXCLUDEFROMCAPTURE not supported on this build)");
        }
        else
        {
            Log.Information("Overlay: WDA_EXCLUDEFROMCAPTURE applied");
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DragMove();
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build src/AIHelperNET.App/
```

- [ ] **Step 4: Commit**

```powershell
git add src/AIHelperNET.App/Windows/OverlayWindow.xaml src/AIHelperNET.App/Windows/OverlayWindow.xaml.cs
git commit -m "feat(app): add OverlayWindow with stealth WDA_EXCLUDEFROMCAPTURE"
```

---

### Task 41: SettingsWindow (basic)

**Files:**
- Create: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`
- Create: `src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs`
- Create: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Create SettingsViewModel**

```csharp
// src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Application.Sessions.Dtos;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed partial class SettingsViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private AppSettingsDto? _settings;
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var result = await mediator.Send(new GetSettingsQuery());
        if (result.IsSuccess) Settings = result.Value;
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput)) return;

        var secure = new System.Security.SecureString();
        foreach (var c in ApiKeyInput) secure.AppendChar(c);
        secure.MakeReadOnly();

        var result = await mediator.Send(new SaveApiKeyCommand(secure));
        StatusMessage = result.IsSuccess ? "API key saved." : $"Error: {string.Join(", ", result.Errors)}";
        ApiKeyInput = string.Empty;
    }
}
```

- [ ] **Step 2: Create SettingsWindow XAML**

```xml
<!-- src/AIHelperNET.App/Windows/SettingsWindow.xaml -->
<Window x:Class="AIHelperNET.App.Windows.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="AIHelper — Settings" Width="480" Height="320"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize">
    <StackPanel Margin="16">
        <TextBlock Text="Claude API Key" FontWeight="Bold" Margin="0,0,0,4"/>
        <DockPanel>
            <Button Content="Save" DockPanel.Dock="Right" Width="60" Margin="8,0,0,0"
                    Command="{Binding SaveApiKeyCommand}"/>
            <PasswordBox x:Name="ApiKeyBox" PasswordChanged="ApiKeyBox_PasswordChanged"/>
        </DockPanel>
        <TextBlock Text="{Binding StatusMessage}" Foreground="DarkGreen" Margin="0,8,0,0" FontSize="11"/>
    </StackPanel>
</Window>
```

- [ ] **Step 3: Create SettingsWindow code-behind**

```csharp
// src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs
using System.Windows;
using AIHelperNET.App.ViewModels;

namespace AIHelperNET.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _vm.LoadAsync();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiKeyInput = ApiKeyBox.Password;
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build src/AIHelperNET.App/
```

- [ ] **Step 5: Commit**

```powershell
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
git commit -m "feat(app): add SettingsWindow and SettingsViewModel"
```

---

### Task 42: App.xaml + HostConfiguration + DI + hotkey wiring

**Files:**
- Modify: `src/AIHelperNET.App/App.xaml`
- Modify: `src/AIHelperNET.App/App.xaml.cs`
- Create: `src/AIHelperNET.App/HostConfiguration.cs`
- Create: `src/AIHelperNET.App/DependencyInjection.cs`

- [ ] **Step 1: Create DependencyInjection (Presentation)**

```csharp
// src/AIHelperNET.App/DependencyInjection.cs
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.App;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());

        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddTransient<OverlayWindow>();
        services.AddTransient<SettingsWindow>();

        return services;
    }
}
```

- [ ] **Step 2: Create HostConfiguration**

```csharp
// src/AIHelperNET.App/HostConfiguration.cs
using AIHelperNET.Application;
using AIHelperNET.Infrastructure;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AIHelperNET.App;

public static class HostConfiguration
{
    public static HostApplicationBuilder ConfigureAIHelper(this HostApplicationBuilder b)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Infrastructure.Common.AppPaths.LogFile,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14)
            .CreateLogger();

        b.Services.AddSerilog();

        b.Services
            .AddApplication()
            .AddInfrastructure(b.Configuration)
            .AddPresentation();

        b.Services.AddSingleton(TimeProvider.System);

        return b;
    }
}
```

- [ ] **Step 3: Update App.xaml — remove StartupUri**

```xml
<!-- src/AIHelperNET.App/App.xaml -->
<Application x:Class="AIHelperNET.App.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources/>
</Application>
```

- [ ] **Step 4: Update App.xaml.cs — IHost startup + hotkey wiring**

```csharp
// src/AIHelperNET.App/App.xaml.cs
using System.Windows;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AIHelperNET.Infrastructure.Persistence;

namespace AIHelperNET.App;

public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureAIHelper();
        _host = builder.Build();

        // Apply EF Core migrations / ensure DB exists
        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();

        // Show overlay
        var overlay = _host.Services.GetRequiredService<OverlayWindow>();
        overlay.Show();

        // Wire hotkeys after overlay window has an HWND
        WireHotkeys(overlay);
    }

    private void WireHotkeys(OverlayWindow overlay)
    {
        var hotkeys = _host.Services.GetRequiredService<IGlobalHotkeyService>() as GlobalHotkeyService;
        if (hotkeys is null) return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
        hotkeys.Initialize(hwnd);

        hotkeys.Register(HotkeyId.ToggleSession,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Space);
        hotkeys.Register(HotkeyId.CaptureScreen,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.S);
        hotkeys.Register(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q);
        hotkeys.Register(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.C);
        hotkeys.Register(HotkeyId.ToggleOverlay,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.H);

        var vm = _host.Services.GetRequiredService<OverlayViewModel>();
        hotkeys.HotkeyPressed += (_, id) =>
        {
            switch (id)
            {
                case HotkeyId.ToggleSession:  _ = vm.ToggleSessionCommand.ExecuteAsync(null); break;
                case HotkeyId.CaptureScreen:  _ = vm.CaptureScreenCommand.ExecuteAsync(null); break;
                case HotkeyId.GenerateAnswer: _ = vm.GenerateAnswerCommand.ExecuteAsync(null); break;
                case HotkeyId.CopyAnswer:     vm.CopyAnswerCommand.Execute(null); break;
                case HotkeyId.ToggleOverlay:  vm.ToggleVisibilityCommand.Execute(null); break;
            }
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}
```

- [ ] **Step 5: Build entire solution**

```powershell
dotnet build AIHelperNET.sln
```

Expected: 0 errors.

- [ ] **Step 6: Run all tests**

```powershell
dotnet test AIHelperNET.sln --logger "console;verbosity=minimal"
```

Expected: All tests pass including architecture tests.

- [ ] **Step 7: Commit**

```powershell
git add src/AIHelperNET.App/
git commit -m "feat(app): complete presentation layer — IHost, OverlayWindow, hotkey wiring"
```

---

### Task 43: Final end-to-end smoke test

This is a manual verification checklist — not automated. Run the app and verify the data flow described in spec §6.4.

- [ ] **Step 1: Launch the app**

```powershell
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj
```

Expected: Overlay window appears (dark, topmost, borderless).

- [ ] **Step 2: Verify stealth overlay**

Open Zoom, Teams, or OBS and share the screen. The overlay should NOT appear in the capture — it should be invisible to other viewers.

- [ ] **Step 3: Verify hotkeys**

| Hotkey | Expected |
|---|---|
| `Ctrl+Shift+Space` | Session starts (green indicator) |
| `Ctrl+Shift+H` | Overlay hides and reappears |
| `Ctrl+Shift+S` | Screen is captured (no crash) |
| `Ctrl+Shift+Q` | Answer generation starts (tokens appear) |
| `Ctrl+Shift+C` | Current answer copied to clipboard |
| `Ctrl+Shift+Space` | Session stops |

- [ ] **Step 4: Verify DB created**

```powershell
Test-Path "$env:LOCALAPPDATA\AIHelperNET\sessions.db"
```

Expected: `True`

- [ ] **Step 5: Verify logs**

```powershell
Get-ChildItem "$env:LOCALAPPDATA\AIHelperNET\logs\"
```

Expected: log file present with session start/stop entries.

- [ ] **Step 6: Final commit**

```powershell
git add -A
git commit -m "feat: AIHelperNET v1 implementation complete"
```

---

## Implementation complete

All 7 phases produce a working live interview copilot:

| Phase | Deliverable |
|---|---|
| 1 | Solution skeleton, all projects, NuGet packages |
| 2 | Domain — Session, entities, QuestionDetector (TDD, no mocks) |
| 3 | Application — CQRS, behaviors, PromptBuilder, ports |
| 4 | Infrastructure — Persistence, secrets, hotkeys |
| 5 | Infrastructure — Audio capture, Whisper transcription |
| 6 | Infrastructure — Claude + Ollama providers, Windows OCR |
| 7 | Presentation — Stealth overlay, ViewModel, hotkey wiring |

Security checklist from spec §8 is satisfied:
- API key: Windows Credential Manager only, transported as `SecureString`, zeroed after use
- No payload logging: `LoggingBehavior` logs type names + timings only
- Stealth overlay: `WDA_EXCLUDEFROMCAPTURE` with `WDA_MONITOR` fallback
- All data local: `%LOCALAPPDATA%\AIHelperNET\`
