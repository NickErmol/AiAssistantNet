# Settings, History & Interaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add named tech-stack presets, audio device selection, Whisper language selection, overlay opacity control, session history browser with export, follow-up questions, screen analysis modes, and audio level meters to AIHelperNET.

**Architecture:** Three sequential groups matching the spec. Group 1 (Settings) touches Application DTOs + Infrastructure + SettingsWindow UI only. Group 2 (History) adds Application query/command/port + a new overlay panel. Group 3 (Interaction) extends ConversationTurnViewModel, PromptBuilderService, and the sidebar. All groups are independently committable.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (source-gen), Mediator, NAudio.CoreAudioApi, Whisper.NET, FluentResults, EF Core/SQLite, xUnit.

---

## File Map

### Group 1 — Settings & Configuration

| Action | Path |
|--------|------|
| Create | `src/AIHelperNET.Application/Sessions/Dtos/ProfilePreset.cs` |
| Modify | `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs` |
| Create | `src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs` |
| Modify | `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs` |
| Modify | `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` |
| Modify | `src/AIHelperNET.App/Services/SessionRunner.cs` |
| Modify | `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` |
| Modify | `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` |
| Modify | `src/AIHelperNET.App/Windows/SettingsWindow.xaml` |
| Modify | `src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs` |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs` |
| Create | `tests/AIHelperNET.Application.Tests/Settings/ProfilePresetTests.cs` |
| Create | `tests/AIHelperNET.Application.Tests/Settings/SaveSettingsCommandTests.cs` |

### Group 2 — Session Lifecycle

| Action | Path |
|--------|------|
| Create | `src/AIHelperNET.Application/Sessions/Queries/GetSessionDetailQuery.cs` |
| Create | `src/AIHelperNET.Application/Abstractions/IExportService.cs` |
| Create | `src/AIHelperNET.Application/Sessions/Commands/ExportSessionCommand.cs` |
| Create | `src/AIHelperNET.Infrastructure/Export/ExportService.cs` |
| Modify | `src/AIHelperNET.Infrastructure/DependencyInjection.cs` |
| Create | `src/AIHelperNET.App/ViewModels/HistoryViewModel.cs` |
| Create | `src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml` |
| Create | `src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml.cs` |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs` |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindowContext.cs` (rename from class in .xaml.cs) |
| Create | `tests/AIHelperNET.Infrastructure.Tests/Export/ExportServiceTests.cs` |

### Group 3 — Overlay & Interaction

| Action | Path |
|--------|------|
| Modify | `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs` |
| Create | `src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs` |
| Create | `src/AIHelperNET.Application/Answers/ScreenAnalysisMode.cs` |
| Modify | `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` |
| Modify | `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` |
| Modify | `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` |
| Modify | `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` |
| Create | `src/AIHelperNET.Application/Abstractions/IAudioLevelMonitor.cs` |
| Create | `src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs` |
| Create | `src/AIHelperNET.App/ViewModels/AudioLevelViewModel.cs` |
| Modify | `src/AIHelperNET.App/App.xaml.cs` |
| Create | `tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs` |

---

## GROUP 1 — Settings & Configuration

---

### Task 1: ProfilePreset DTO and AppSettingsDto extensions

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Dtos/ProfilePreset.cs`
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Modify: `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`
- Test: `tests/AIHelperNET.Application.Tests/Settings/ProfilePresetTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AIHelperNET.Application.Tests/Settings/ProfilePresetTests.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Tests.Settings;

public sealed class ProfilePresetTests
{
    [Fact]
    public void ProfilePreset_RoundTripsViaJsonSerializer()
    {
        var preset = new ProfilePreset(
            "C# Azure",
            new CodeProfile("C#", "ASP.NET Core", "Angular", "SQL Server",
                "Azure", null, "Clean", "xUnit", null),
            AnswerSettings.Default);

        var json = System.Text.Json.JsonSerializer.Serialize(preset);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProfilePreset>(json);

        Assert.NotNull(restored);
        Assert.Equal("C# Azure", restored.Name);
        Assert.Equal("C#", restored.CodeProfile.ProgrammingLanguage);
    }

    [Fact]
    public void AppSettingsDto_HasDefaultWhisperLanguage()
    {
        var dto = new AppSettingsDto(
            AiBackend.Claude,
            WhisperModelSize.Base,
            AnswerSettings.Default,
            CodeProfile.Empty,
            null, null);

        Assert.Equal("auto", dto.WhisperLanguage);
        Assert.Equal(0.75, dto.OverlayOpacity);
        Assert.Empty(dto.Presets);
    }
}
```

- [ ] **Step 2: Run test — expect compile failure (ProfilePreset not defined)**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ProfilePresetTests"
```

- [ ] **Step 3: Create ProfilePreset DTO**

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/ProfilePreset.cs
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>A named snapshot of CodeProfile + AnswerSettings for quick switching between interview types.</summary>
public sealed record ProfilePreset(
    string Name,
    CodeProfile CodeProfile,
    AnswerSettings AnswerSettings);
```

- [ ] **Step 4: Extend AppSettingsDto**

Replace the existing record with:

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Serialisable application settings shared between the UI and the settings store.</summary>
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId,
    int AnswerFontSize = 12,
    string WhisperLanguage = "auto",
    double OverlayOpacity = 0.75)
{
    /// <summary>Named setting presets for quick profile switching.</summary>
    public IReadOnlyList<ProfilePreset> Presets { get; init; } = [];
}
```

- [ ] **Step 5: Update DefaultSettings in JsonSettingsStore**

The existing `DefaultSettings()` call in `JsonSettingsStore` creates `AppSettingsDto` with 6 positional args — that still compiles because new params have defaults. Verify no compile error by doing a build. No code change needed here.

- [ ] **Step 6: Run tests — expect pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ProfilePresetTests"
```

Expected: all pass.

- [ ] **Step 7: Build whole solution**

```
dotnet build
```

Expected: 0 errors, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 8: Commit**

```
git add src/AIHelperNET.Application/Sessions/Dtos/ProfilePreset.cs
git add src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs
git add tests/AIHelperNET.Application.Tests/Settings/ProfilePresetTests.cs
git commit -m "feat: add ProfilePreset DTO and extend AppSettingsDto with WhisperLanguage, OverlayOpacity, Presets"
```

---

### Task 2: SaveSettingsCommand

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs`
- Test: `tests/AIHelperNET.Application.Tests/Settings/SaveSettingsCommandTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// tests/AIHelperNET.Application.Tests/Settings/SaveSettingsCommandTests.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using NSubstitute;

namespace AIHelperNET.Application.Tests.Settings;

public sealed class SaveSettingsCommandTests
{
    [Fact]
    public async Task Handle_CallsSaveAsync_WithProvidedSettings()
    {
        var store = Substitute.For<ISettingsStore>();
        var handler = new SaveSettingsHandler(store);
        var settings = new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Base,
            AnswerSettings.Default, CodeProfile.Empty,
            null, null);

        var result = await handler.Handle(new SaveSettingsCommand(settings), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await store.Received(1).SaveAsync(settings, Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run test — expect compile failure**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveSettingsCommandTests"
```

- [ ] **Step 3: Create SaveSettingsCommand**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Persists the full application settings snapshot.</summary>
public sealed record SaveSettingsCommand(AppSettingsDto Settings) : IRequest<Result>;

/// <summary>Handles <see cref="SaveSettingsCommand"/>.</summary>
public sealed class SaveSettingsHandler(ISettingsStore settingsStore)
    : IRequestHandler<SaveSettingsCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(SaveSettingsCommand cmd, CancellationToken ct)
    {
        await settingsStore.SaveAsync(cmd.Settings, ct);
        return Result.Ok();
    }
}
```

- [ ] **Step 4: Run test — expect pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveSettingsCommandTests"
```

- [ ] **Step 5: Build solution**

```
dotnet build
```

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs
git add tests/AIHelperNET.Application.Tests/Settings/SaveSettingsCommandTests.cs
git commit -m "feat: add SaveSettingsCommand for persisting full AppSettingsDto"
```

---

### Task 3: Whisper language parameter

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs`
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`

- [ ] **Step 1: Update ITranscriptionService interface**

```csharp
// src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs
namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for transcribing audio frames to text segments.</summary>
public interface ITranscriptionService
{
    /// <summary>Transcribes an audio stream to transcript segments.</summary>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        string language,
        CancellationToken ct);
}
```

- [ ] **Step 2: Update WhisperTranscriptionService**

Change the method signature and replace the hardcoded `.WithLanguage("en")`:

```csharp
// src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
// Change the method signature from:
public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
    IAsyncEnumerable<AudioFrame> frames,
    WhisperModelSize model,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)

// To:
public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
    IAsyncEnumerable<AudioFrame> frames,
    WhisperModelSize model,
    string language,
    [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
```

Change the processor builder from:

```csharp
await using var processor = factory.CreateBuilder()
    .WithLanguage("en")
```

To:

```csharp
var lang = string.IsNullOrWhiteSpace(language) || language == "auto" ? null : language;
await using var processor = factory.CreateBuilder()
    .WithLanguage(lang ?? "en")
```

- [ ] **Step 3: Update SessionRunner.StartAsync and RunAsync**

In `SessionRunner.StartAsync`, add `string language` parameter after `WhisperModelSize model`:

```csharp
public async Task StartAsync(
    SessionId sessionId,
    AudioDeviceSelection devices,
    WhisperModelSize model,
    string language,
    AudioSourceMode audioSource)
```

Pass `language` to `RunAsync`:

```csharp
_pipelineTask = RunAsync(result.Value, devices, model, language, audioSource, _cts.Token);
```

In `RunAsync`, add `string language` parameter and pass it to both `TranscribeAsync` calls:

```csharp
private async Task RunAsync(
    Session session,
    AudioDeviceSelection devices,
    WhisperModelSize model,
    string language,
    AudioSourceMode audioSource,
    CancellationToken ct)
```

Find the two `TranscribeAsync` calls (one for mic, one for loopback) and add `language` as the third argument:

```csharp
await foreach (var seg in transcription
    .TranscribeAsync(micChannel.Reader.ReadAllAsync(ct), model, language, ct)
    .WithCancellation(ct))
```

```csharp
await foreach (var seg in transcription
    .TranscribeAsync(loopbackChannel.Reader.ReadAllAsync(ct), model, language, ct)
    .WithCancellation(ct))
```

- [ ] **Step 4: Update SessionControlViewModel.ToggleSessionAsync**

Find the call to `runner.StartAsync` in `ToggleSessionAsync` (currently passes `settings?.WhisperModel ?? WhisperModelSize.Base` as the model arg). Add the language argument after it:

```csharp
await runner.StartAsync(
    sessionId,
    new AudioDeviceSelection(settings?.MicDeviceId, settings?.LoopbackDeviceId),
    settings?.WhisperModel ?? WhisperModelSize.Base,
    settings?.WhisperLanguage ?? "auto",
    AudioSource);
```

- [ ] **Step 5: Build solution — expect 0 errors**

```
dotnet build
```

- [ ] **Step 6: Run all tests**

```
dotnet test
```

- [ ] **Step 7: Commit**

```
git add src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs
git add src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
git add src/AIHelperNET.App/Services/SessionRunner.cs
git add src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs
git commit -m "feat: thread WhisperLanguage setting from AppSettingsDto through SessionRunner to transcription"
```

---

### Task 4: SettingsViewModel — extended for all 4 tabs

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Replace SettingsViewModel with the extended version**

```csharp
// src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
using System.Collections.ObjectModel;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

/// <summary>Backing ViewModel for all four tabs of SettingsWindow.</summary>
public sealed partial class SettingsViewModel(IMediator mediator) : ObservableObject
{
    // ── API Key tab ───────────────────────────────────────────────
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── Audio tab ─────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedMicDeviceId;
    [ObservableProperty] private string? _selectedLoopbackDeviceId;
    [ObservableProperty] private string _whisperLanguage = "auto";

    // ── Code Profiles tab ─────────────────────────────────────────
    [ObservableProperty] private ProfilePreset? _selectedPreset;
    [ObservableProperty] private string _presetName = string.Empty;

    // CodeProfile fields
    [ObservableProperty] private string _programmingLanguage = string.Empty;
    [ObservableProperty] private string _backendFramework    = string.Empty;
    [ObservableProperty] private string _frontendFramework   = string.Empty;
    [ObservableProperty] private string _database            = string.Empty;
    [ObservableProperty] private string _cloudDevOps         = string.Empty;
    [ObservableProperty] private string _messaging           = string.Empty;
    [ObservableProperty] private string _architectureStyle   = string.Empty;
    [ObservableProperty] private string _testingFramework    = string.Empty;
    [ObservableProperty] private string _customNotes         = string.Empty;

    // AnswerSettings fields
    [ObservableProperty] private AnswerLength     _answerLength     = AnswerLength.ShortLength;
    [ObservableProperty] private AnswerComplexity _answerComplexity = AnswerComplexity.Balanced;
    [ObservableProperty] private AnswerStyle      _answerStyle      = AnswerStyle.Interview;
    [ObservableProperty] private AnswerTone       _answerTone       = AnswerTone.Confident;
    [ObservableProperty] private AnswerFormat     _answerFormat     = AnswerFormat.VerbalOnly;
    [ObservableProperty] private string           _outputLanguage   = "English";

    /// <summary>All saved presets.</summary>
    public ObservableCollection<ProfilePreset> Presets { get; } = [];

    // ── Appearance tab ────────────────────────────────────────────
    [ObservableProperty] private double _overlayOpacity = 0.75;

    /// <summary>Raised when opacity changes so MainOverlayWindow can update live.</summary>
    public event Action<double>? OpacityChanged;

    partial void OnOverlayOpacityChanged(double value) => OpacityChanged?.Invoke(value);

    // ── Load ──────────────────────────────────────────────────────
    [RelayCommand]
    public async Task LoadAsync()
    {
        var result = await mediator.Send(new GetSettingsQuery());
        if (!result.IsSuccess) return;
        var s = result.Value;

        SelectedMicDeviceId      = s.MicDeviceId;
        SelectedLoopbackDeviceId = s.LoopbackDeviceId;
        WhisperLanguage          = s.WhisperLanguage;
        OverlayOpacity           = s.OverlayOpacity;

        ProgrammingLanguage = s.CodeProfile.ProgrammingLanguage ?? string.Empty;
        BackendFramework    = s.CodeProfile.BackendFramework    ?? string.Empty;
        FrontendFramework   = s.CodeProfile.FrontendFramework   ?? string.Empty;
        Database            = s.CodeProfile.Database            ?? string.Empty;
        CloudDevOps         = s.CodeProfile.CloudDevOps         ?? string.Empty;
        Messaging           = s.CodeProfile.Messaging           ?? string.Empty;
        ArchitectureStyle   = s.CodeProfile.ArchitectureStyle   ?? string.Empty;
        TestingFramework    = s.CodeProfile.TestingFramework    ?? string.Empty;
        CustomNotes         = s.CodeProfile.CustomNotes         ?? string.Empty;

        AnswerLength     = s.AnswerSettings.Length;
        AnswerComplexity = s.AnswerSettings.Complexity;
        AnswerStyle      = s.AnswerSettings.Style;
        AnswerTone       = s.AnswerSettings.Tone;
        AnswerFormat     = s.AnswerSettings.Format;
        OutputLanguage   = s.AnswerSettings.OutputLanguage;

        Presets.Clear();
        foreach (var p in s.Presets) Presets.Add(p);

        await RefreshKeyStatusAsync();
    }

    // ── API Key commands ──────────────────────────────────────────
    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput)) return;
        var secure = new System.Security.SecureString();
        foreach (var c in ApiKeyInput) secure.AppendChar(c);
        secure.MakeReadOnly();
        var result = await mediator.Send(new SaveApiKeyCommand(secure));
        StatusMessage = result.IsSuccess ? "API key saved ✓" : $"Error: {string.Join(", ", result.Errors)}";
        ApiKeyInput   = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteApiKeyAsync()
    {
        var result = await mediator.Send(new DeleteApiKeyCommand());
        StatusMessage = result.IsSuccess ? "API key deleted." : $"Error: {string.Join(", ", result.Errors)}";
    }

    // ── Save all settings ─────────────────────────────────────────
    [RelayCommand]
    public async Task SaveSettingsAsync()
    {
        var existing = await mediator.Send(new GetSettingsQuery());
        var current  = existing.IsSuccess ? existing.Value : null;

        var dto = new AppSettingsDto(
            current?.ActiveBackend  ?? AiBackend.Claude,
            current?.WhisperModel   ?? WhisperModelSize.Base,
            new AnswerSettings(AnswerLength, AnswerComplexity, AnswerStyle, AnswerTone, AnswerFormat, OutputLanguage),
            new CodeProfile(NullIfEmpty(ProgrammingLanguage), NullIfEmpty(BackendFramework),
                NullIfEmpty(FrontendFramework), NullIfEmpty(Database), NullIfEmpty(CloudDevOps),
                NullIfEmpty(Messaging), NullIfEmpty(ArchitectureStyle), NullIfEmpty(TestingFramework),
                NullIfEmpty(CustomNotes)),
            NullIfEmpty(SelectedMicDeviceId),
            NullIfEmpty(SelectedLoopbackDeviceId),
            current?.AnswerFontSize ?? 12,
            WhisperLanguage,
            OverlayOpacity)
        {
            Presets = [.. Presets]
        };

        await mediator.Send(new SaveSettingsCommand(dto));
        StatusMessage = "Settings saved ✓";
    }

    // ── Preset management ─────────────────────────────────────────
    [RelayCommand]
    private void LoadPreset(ProfilePreset? preset)
    {
        if (preset is null) return;
        ProgrammingLanguage = preset.CodeProfile.ProgrammingLanguage ?? string.Empty;
        BackendFramework    = preset.CodeProfile.BackendFramework    ?? string.Empty;
        FrontendFramework   = preset.CodeProfile.FrontendFramework   ?? string.Empty;
        Database            = preset.CodeProfile.Database            ?? string.Empty;
        CloudDevOps         = preset.CodeProfile.CloudDevOps         ?? string.Empty;
        Messaging           = preset.CodeProfile.Messaging           ?? string.Empty;
        ArchitectureStyle   = preset.CodeProfile.ArchitectureStyle   ?? string.Empty;
        TestingFramework    = preset.CodeProfile.TestingFramework    ?? string.Empty;
        CustomNotes         = preset.CodeProfile.CustomNotes         ?? string.Empty;
        AnswerLength     = preset.AnswerSettings.Length;
        AnswerComplexity = preset.AnswerSettings.Complexity;
        AnswerStyle      = preset.AnswerSettings.Style;
        AnswerTone       = preset.AnswerSettings.Tone;
        AnswerFormat     = preset.AnswerSettings.Format;
        OutputLanguage   = preset.AnswerSettings.OutputLanguage;
        PresetName       = preset.Name;
    }

    [RelayCommand]
    private async Task SaveAsNewPresetAsync()
    {
        if (string.IsNullOrWhiteSpace(PresetName)) return;
        var preset = BuildCurrentPreset();
        Presets.Add(preset);
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private async Task UpdateCurrentPresetAsync()
    {
        if (SelectedPreset is null) return;
        var idx = Presets.IndexOf(SelectedPreset);
        if (idx < 0) return;
        Presets[idx] = BuildCurrentPreset() with { Name = SelectedPreset.Name };
        SelectedPreset = Presets[idx];
        await SaveSettingsAsync();
    }

    [RelayCommand]
    private async Task DeletePresetAsync(ProfilePreset? preset)
    {
        if (preset is null) return;
        Presets.Remove(preset);
        await SaveSettingsAsync();
    }

    private ProfilePreset BuildCurrentPreset() => new(
        PresetName,
        new CodeProfile(NullIfEmpty(ProgrammingLanguage), NullIfEmpty(BackendFramework),
            NullIfEmpty(FrontendFramework), NullIfEmpty(Database), NullIfEmpty(CloudDevOps),
            NullIfEmpty(Messaging), NullIfEmpty(ArchitectureStyle), NullIfEmpty(TestingFramework),
            NullIfEmpty(CustomNotes)),
        new AnswerSettings(AnswerLength, AnswerComplexity, AnswerStyle, AnswerTone, AnswerFormat, OutputLanguage));

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private async Task RefreshKeyStatusAsync()
    {
        var hasKey = await mediator.Send(new HasApiKeyQuery());
        if (StatusMessage == string.Empty || StatusMessage.StartsWith("API key is", StringComparison.Ordinal))
            StatusMessage = (hasKey.IsSuccess && hasKey.Value)
                ? "API key is stored ✓" : "No API key stored — enter one above.";
    }
}
```

- [ ] **Step 2: Build solution — expect 0 errors**

```
dotnet build
```

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
git commit -m "feat: extend SettingsViewModel with audio, code profile, answer settings, preset and opacity tabs"
```

---

### Task 5: SettingsWindow XAML — 4-tab layout

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs`

- [ ] **Step 1: Replace SettingsWindow.xaml**

Replace the entire file content:

```xml
<Window x:Class="AIHelperNET.App.Windows.SettingsWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:AIHelperNET.App.ViewModels"
        Title="AIHelper — Settings" Width="560" Height="600"
        WindowStartupLocation="CenterScreen" ResizeMode="NoResize"
        Background="{DynamicResource Brush.Background.Window}"
        Loaded="Window_Loaded">

    <DockPanel Margin="12">
        <Button DockPanel.Dock="Bottom"
                Content="Save"
                HorizontalAlignment="Right"
                Width="80" Margin="0,8,0,0"
                Command="{Binding SaveSettingsCommand}"/>

        <TabControl>
            <!-- ═══ API Key ═══ -->
            <TabItem Header="API Key">
                <StackPanel Margin="8">
                    <TextBlock Text="Claude API Key" FontWeight="SemiBold"
                               FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Primary}"
                               Margin="0,0,0,6"/>
                    <DockPanel>
                        <Button Content="Delete" DockPanel.Dock="Right" Width="60"
                                Foreground="#CC4444"
                                Command="{Binding DeleteApiKeyCommand}"/>
                        <Button Content="Save" DockPanel.Dock="Right" Width="60" Margin="0,0,6,0"
                                Command="{Binding SaveApiKeyCommand}"/>
                        <Border Background="{DynamicResource Brush.Background.Panel}"
                                BorderBrush="{DynamicResource Brush.Border}"
                                BorderThickness="1" CornerRadius="3" Padding="1">
                            <PasswordBox x:Name="ApiKeyBox"
                                         Background="Transparent"
                                         Foreground="{DynamicResource Brush.Foreground.Primary}"
                                         CaretBrush="{DynamicResource Brush.Foreground.Primary}"
                                         BorderThickness="0" Padding="5,3"
                                         PasswordChanged="ApiKeyBox_PasswordChanged"/>
                        </Border>
                    </DockPanel>
                    <TextBlock Text="{Binding StatusMessage}"
                               Foreground="{DynamicResource Brush.Semantic.Active}"
                               Margin="0,8,0,0"
                               FontSize="{DynamicResource Font.SM}"/>
                </StackPanel>
            </TabItem>

            <!-- ═══ Audio ═══ -->
            <TabItem Header="Audio">
                <StackPanel Margin="8">
                    <TextBlock Text="AUDIO DEVICES" Style="{StaticResource SectionLabel}" Margin="0,0,0,6"/>
                    <TextBlock Text="Microphone" FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}" Margin="0,0,0,2"/>
                    <ComboBox x:Name="MicCombo"
                              DisplayMemberPath="FriendlyName"
                              SelectedValuePath="Id"
                              SelectedValue="{Binding SelectedMicDeviceId}"
                              Margin="0,0,0,8"/>
                    <TextBlock Text="System Audio (Loopback)" FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}" Margin="0,0,0,2"/>
                    <ComboBox x:Name="LoopbackCombo"
                              DisplayMemberPath="FriendlyName"
                              SelectedValuePath="Id"
                              SelectedValue="{Binding SelectedLoopbackDeviceId}"
                              Margin="0,0,0,16"/>
                    <TextBlock Text="TRANSCRIPTION" Style="{StaticResource SectionLabel}" Margin="0,0,0,6"/>
                    <TextBlock Text="Language" FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}" Margin="0,0,0,2"/>
                    <ComboBox x:Name="LanguageCombo"
                              SelectedValue="{Binding WhisperLanguage}">
                        <ComboBoxItem Content="Auto-detect" Tag="auto"/>
                        <ComboBoxItem Content="English"     Tag="en"/>
                        <ComboBoxItem Content="Russian"     Tag="ru"/>
                        <ComboBoxItem Content="Polish"      Tag="pl"/>
                    </ComboBox>
                </StackPanel>
            </TabItem>

            <!-- ═══ Code Profiles ═══ -->
            <TabItem Header="Code Profiles">
                <ScrollViewer VerticalScrollBarVisibility="Auto">
                    <StackPanel Margin="8">
                        <DockPanel Margin="0,0,0,8">
                            <Button Content="Delete" DockPanel.Dock="Right" Width="55" Margin="4,0,0,0"
                                    Command="{Binding DeletePresetCommand}"
                                    CommandParameter="{Binding SelectedPreset}"/>
                            <Button Content="Load"   DockPanel.Dock="Right" Width="55" Margin="4,0,0,0"
                                    Command="{Binding LoadPresetCommand}"
                                    CommandParameter="{Binding SelectedPreset}"/>
                            <ComboBox ItemsSource="{Binding Presets}"
                                      SelectedItem="{Binding SelectedPreset}"
                                      DisplayMemberPath="Name"/>
                        </DockPanel>
                        <Separator Margin="0,0,0,8"/>
                        <TextBlock Text="TECH STACK" Style="{StaticResource SectionLabel}" Margin="0,0,0,6"/>
                        <TextBlock Text="Language"   Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding ProgrammingLanguage}" Margin="0,0,0,4"/>
                        <TextBlock Text="Backend"    Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding BackendFramework}"    Margin="0,0,0,4"/>
                        <TextBlock Text="Frontend"   Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding FrontendFramework}"   Margin="0,0,0,4"/>
                        <TextBlock Text="Database"   Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding Database}"            Margin="0,0,0,4"/>
                        <TextBlock Text="Cloud / DevOps" Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding CloudDevOps}"         Margin="0,0,0,4"/>
                        <TextBlock Text="Messaging"  Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding Messaging}"           Margin="0,0,0,4"/>
                        <TextBlock Text="Architecture" Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding ArchitectureStyle}"   Margin="0,0,0,4"/>
                        <TextBlock Text="Testing"    Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding TestingFramework}"    Margin="0,0,0,4"/>
                        <TextBlock Text="Notes"      Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding CustomNotes}"         Margin="0,0,0,12" Height="48"
                                 AcceptsReturn="True" TextWrapping="Wrap"/>
                        <TextBlock Text="ANSWER STYLE" Style="{StaticResource SectionLabel}" Margin="0,0,0,6"/>
                        <TextBlock Text="Length"     Style="{StaticResource FieldLabel}"/>
                        <ComboBox SelectedValue="{Binding AnswerLength}"
                                  SelectedValuePath="Tag" Margin="0,0,0,4">
                            <ComboBoxItem Content="Very Short" Tag="{x:Static vm:AnswerLengthProxy.VeryShort}"/>
                            <ComboBoxItem Content="Short"      Tag="{x:Static vm:AnswerLengthProxy.ShortLength}"/>
                            <ComboBoxItem Content="Medium"     Tag="{x:Static vm:AnswerLengthProxy.Medium}"/>
                            <ComboBoxItem Content="Detailed"   Tag="{x:Static vm:AnswerLengthProxy.Detailed}"/>
                            <ComboBoxItem Content="Deep Dive"  Tag="{x:Static vm:AnswerLengthProxy.DeepDive}"/>
                        </ComboBox>
                        <TextBlock Text="Complexity"  Style="{StaticResource FieldLabel}"/>
                        <ComboBox SelectedValue="{Binding AnswerComplexity}"
                                  SelectedValuePath="Tag" Margin="0,0,0,4">
                            <ComboBoxItem Content="Simple"   Tag="{x:Static vm:AnswerComplexityProxy.Simple}"/>
                            <ComboBoxItem Content="Balanced" Tag="{x:Static vm:AnswerComplexityProxy.Balanced}"/>
                            <ComboBoxItem Content="Advanced" Tag="{x:Static vm:AnswerComplexityProxy.Advanced}"/>
                            <ComboBoxItem Content="Senior"   Tag="{x:Static vm:AnswerComplexityProxy.Senior}"/>
                        </ComboBox>
                        <TextBlock Text="Style"       Style="{StaticResource FieldLabel}"/>
                        <ComboBox SelectedValue="{Binding AnswerStyle}"
                                  SelectedValuePath="Tag" Margin="0,0,0,4">
                            <ComboBoxItem Content="Natural"      Tag="{x:Static vm:AnswerStyleProxy.Natural}"/>
                            <ComboBoxItem Content="Interview"    Tag="{x:Static vm:AnswerStyleProxy.Interview}"/>
                            <ComboBoxItem Content="Technical"    Tag="{x:Static vm:AnswerStyleProxy.Technical}"/>
                            <ComboBoxItem Content="Step-by-Step" Tag="{x:Static vm:AnswerStyleProxy.StepByStep}"/>
                            <ComboBoxItem Content="Code First"   Tag="{x:Static vm:AnswerStyleProxy.CodeFirst}"/>
                            <ComboBoxItem Content="Architecture" Tag="{x:Static vm:AnswerStyleProxy.Architecture}"/>
                            <ComboBoxItem Content="Debugging"    Tag="{x:Static vm:AnswerStyleProxy.Debugging}"/>
                        </ComboBox>
                        <TextBlock Text="Output Language" Style="{StaticResource FieldLabel}"/>
                        <TextBox Text="{Binding OutputLanguage}" Margin="0,0,0,12"/>
                        <Separator Margin="0,0,0,8"/>
                        <DockPanel>
                            <Button Content="Update current" DockPanel.Dock="Right" Margin="4,0,0,0"
                                    Command="{Binding UpdateCurrentPresetCommand}"/>
                            <TextBox Text="{Binding PresetName}" Margin="0,0,4,0"
                                     PlaceholderText="Preset name..."/>
                        </DockPanel>
                        <Button Content="Save as new preset" HorizontalAlignment="Left"
                                Margin="0,4,0,0"
                                Command="{Binding SaveAsNewPresetCommand}"/>
                    </StackPanel>
                </ScrollViewer>
            </TabItem>

            <!-- ═══ Appearance ═══ -->
            <TabItem Header="Appearance">
                <StackPanel Margin="8">
                    <TextBlock Text="OVERLAY" Style="{StaticResource SectionLabel}" Margin="0,0,0,8"/>
                    <TextBlock Text="Opacity" FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}" Margin="0,0,0,4"/>
                    <Grid>
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="Auto"/>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <TextBlock Grid.Column="0" Text="20%"
                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                   FontSize="{DynamicResource Font.XS}" Margin="0,0,6,0"/>
                        <Slider Grid.Column="1"
                                Minimum="0.2" Maximum="1.0" SmallChange="0.05"
                                Value="{Binding OverlayOpacity}"/>
                        <TextBlock Grid.Column="2" Text="100%"
                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                   FontSize="{DynamicResource Font.XS}" Margin="6,0,0,0"/>
                    </Grid>
                    <TextBlock Text="{Binding OverlayOpacity, StringFormat='{}{0:P0}'}"
                               HorizontalAlignment="Center"
                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                               FontSize="{DynamicResource Font.SM}" Margin="0,4,0,0"/>
                </StackPanel>
            </TabItem>
        </TabControl>
    </DockPanel>
</Window>
```

> **Note on enum bindings:** WPF cannot bind directly to enum values in XAML `Tag` properties using `{x:Static}` in a portable way. Use the `ObjectToObjectConverter` pattern or bind `SelectedItem` and use a converter. The `AnswerLengthProxy` approach shown above requires creating static proxy classes. The simpler alternative for the implementer is to use `ComboBox` with `ItemsSource` bound to `Enum.GetValues()` in code-behind, shown in Step 2.

- [ ] **Step 2: Simplify enum ComboBoxes in code-behind**

In `SettingsWindow.xaml.cs`, populate the enum combos in `Window_Loaded` and remove the `{x:Static}` bindings from XAML (replace those ComboBoxes with plain `ComboBox ItemsSource` bindings):

For each enum combo, in XAML replace the hard-coded items with:
```xml
<ComboBox ItemsSource="{Binding Source={x:Static local:EnumValues.AnswerLengths}}"
          DisplayMemberPath="Display"
          SelectedValuePath="Value"
          SelectedValue="{Binding AnswerLength}"/>
```

And in a new file `src/AIHelperNET.App/Windows/EnumValues.cs`:

```csharp
// src/AIHelperNET.App/Windows/EnumValues.cs
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.App.Windows;

public static class EnumValues
{
    public static IReadOnlyList<(string Display, AnswerLength Value)> AnswerLengths { get; } =
    [
        ("Very Short", AnswerLength.VeryShort),
        ("Short",      AnswerLength.ShortLength),
        ("Medium",     AnswerLength.Medium),
        ("Detailed",   AnswerLength.Detailed),
        ("Deep Dive",  AnswerLength.DeepDive),
    ];

    public static IReadOnlyList<(string Display, AnswerComplexity Value)> AnswerComplexities { get; } =
    [
        ("Simple",   AnswerComplexity.Simple),
        ("Balanced", AnswerComplexity.Balanced),
        ("Advanced", AnswerComplexity.Advanced),
        ("Senior",   AnswerComplexity.Senior),
    ];

    public static IReadOnlyList<(string Display, AnswerStyle Value)> AnswerStyles { get; } =
    [
        ("Natural",       AnswerStyle.Natural),
        ("Interview",     AnswerStyle.Interview),
        ("Technical",     AnswerStyle.Technical),
        ("Step-by-Step",  AnswerStyle.StepByStep),
        ("Code First",    AnswerStyle.CodeFirst),
        ("Architecture",  AnswerStyle.Architecture),
        ("Debugging",     AnswerStyle.Debugging),
    ];
}
```

Also add a `FieldLabel` style to `Styles.xaml`:

```xml
<Style x:Key="FieldLabel" TargetType="TextBlock" BasedOn="{StaticResource {x:Type TextBlock}}">
    <Setter Property="FontSize" Value="{DynamicResource Font.SM}"/>
    <Setter Property="Foreground" Value="{DynamicResource Brush.Foreground.Secondary}"/>
    <Setter Property="Margin" Value="0,4,0,2"/>
</Style>
```

- [ ] **Step 3: Update SettingsWindow.xaml.cs**

```csharp
// src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs
using System.Collections.ObjectModel;
using System.Windows;
using AIHelperNET.App.ViewModels;
using NAudio.CoreAudioApi;

namespace AIHelperNET.App.Windows;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateAudioDevices();
        await _vm.LoadAsync();
    }

    private void PopulateAudioDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        var mics = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        MicCombo.ItemsSource = mics.Select(d => new AudioDeviceItem(d.ID, d.FriendlyName)).ToList();

        var loopbacks = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        LoopbackCombo.ItemsSource = loopbacks.Select(d => new AudioDeviceItem(d.ID, d.FriendlyName)).ToList();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiKeyInput = ApiKeyBox.Password;
}

public sealed record AudioDeviceItem(string Id, string FriendlyName);
```

- [ ] **Step 4: Build solution**

```
dotnet build
```

Fix any binding or namespace errors that arise.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs
git add src/AIHelperNET.App/Windows/EnumValues.cs
git add src/AIHelperNET.App/Resources/Styles.xaml
git commit -m "feat: expand SettingsWindow to 4 tabs — API Key, Audio, Code Profiles, Appearance"
```

---

### Task 6: MainOverlayWindow — apply opacity from settings on startup

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

- [ ] **Step 1: Wire up opacity on window load and live preview**

In `MainOverlayWindow`, inject `SettingsViewModel` and subscribe to `OpacityChanged`. Also load the persisted opacity on window creation.

Add a parameter to the constructor:

```csharp
private readonly SettingsViewModel _settingsVm;

public MainOverlayWindow(
    MainOverlayWindowContext context,
    SettingsWindow settingsWindow,
    SettingsViewModel settingsVm)
{
    InitializeComponent();
    DataContext      = context;
    _settingsWindow  = settingsWindow;
    _settingsVm      = settingsVm;
    _settingsVm.OpacityChanged += opacity => Opacity = opacity;
}
```

In `OnSourceInitialized`, load the persisted opacity before anything else:

```csharp
protected override async void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    // Load persisted opacity before stealth so we don't overwrite it
    var settings = await _settingsVm.LoadAsync().ContinueWith(_ => _settingsVm.OverlayOpacity);
    Opacity = _settingsVm.OverlayOpacity;
    ApplyStealth(enable: true);
}
```

> The `SettingsViewModel.LoadAsync` is already idempotent — safe to call again here.

- [ ] **Step 2: Update DI registration in App.xaml.cs**

Find where `MainOverlayWindow` is constructed and add `SettingsViewModel` as the third argument. The DI container already knows about `SettingsViewModel` if it is registered as a singleton — verify in the service registration file (likely `App.xaml.cs` or a `ServiceCollectionExtensions`).

- [ ] **Step 3: Build and run**

```
dotnet build
dotnet run --project src/AIHelperNET.App
```

Open Settings → Appearance tab → drag slider → confirm overlay transparency changes live.

- [ ] **Step 4: Commit**

```
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs
git commit -m "feat: initialize overlay opacity from persisted settings, update live on slider drag"
```

---

## GROUP 2 — Session Lifecycle

---

### Task 7: GetSessionDetailQuery — full session data for history expand

**Files:**
- Create: `src/AIHelperNET.Application/Sessions/Queries/GetSessionDetailQuery.cs`
- Create: `src/AIHelperNET.Application/Sessions/Dtos/SessionDetailDto.cs`
- Modify: `src/AIHelperNET.Application/Abstractions/ISessionRepository.cs`
- Modify: `src/AIHelperNET.Infrastructure/Persistence/SessionRepository.cs`

- [ ] **Step 1: Define SessionDetailDto**

```csharp
// src/AIHelperNET.Application/Sessions/Dtos/SessionDetailDto.cs
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Sessions.Dtos;

public sealed record TranscriptItemDto(
    string Speaker,
    string Text,
    DateTimeOffset Timestamp);

public sealed record AnswerDto(
    string QuestionText,
    string AnswerText,
    DateTimeOffset CreatedAt);

public sealed record SessionDetailDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Mode,
    IReadOnlyList<TranscriptItemDto> Transcript,
    IReadOnlyList<AnswerDto> Answers);
```

- [ ] **Step 2: Add GetDetailAsync to ISessionRepository**

```csharp
// append to ISessionRepository.cs
/// <summary>Returns full transcript and answers for a single session.</summary>
Task<SessionDetailDto?> GetDetailAsync(SessionId id, CancellationToken ct);
```

- [ ] **Step 3: Implement in SessionRepository**

In `SessionRepository.cs`, add:

```csharp
public async Task<SessionDetailDto?> GetDetailAsync(SessionId id, CancellationToken ct)
{
    var session = await _context.Sessions
        .Include(s => s.TranscriptItems)
        .Include(s => s.Questions)
        .Include(s => s.ConversationTurns)
            .ThenInclude(t => t.AnswerVersions)
        .FirstOrDefaultAsync(s => s.Id == id, ct);

    if (session is null) return null;

    var transcript = session.TranscriptItems
        .OrderBy(i => i.CapturedAt)
        .Select(i => new TranscriptItemDto(
            i.Speaker.ToString(),
            i.Text,
            i.CapturedAt))
        .ToList();

    var answers = session.ConversationTurns
        .OrderBy(t => t.CreatedAt)
        .SelectMany(turn =>
        {
            var q = session.Questions.FirstOrDefault(q => q.Id == turn.InitialQuestionId);
            return turn.AnswerVersions
                .OrderByDescending(v => v.CreatedAt)
                .Take(1)
                .Select(v => new AnswerDto(
                    q?.Text ?? string.Empty,
                    v.Text,
                    v.CreatedAt));
        })
        .ToList();

    return new SessionDetailDto(
        session.Id,
        session.StartedAt,
        session.EndedAt,
        session.Mode.ToString(),
        transcript,
        answers);
}
```

> **Note:** Adjust property names to match your actual EF entity shape. Check `AppDbContext` for the exact navigation property names.

- [ ] **Step 4: Create GetSessionDetailQuery**

```csharp
// src/AIHelperNET.Application/Sessions/Queries/GetSessionDetailQuery.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Queries;

public sealed record GetSessionDetailQuery(SessionId Id) : IRequest<Result<SessionDetailDto?>>;

public sealed class GetSessionDetailHandler(ISessionRepository repository)
    : IRequestHandler<GetSessionDetailQuery, Result<SessionDetailDto?>>
{
    public async ValueTask<Result<SessionDetailDto?>> Handle(
        GetSessionDetailQuery query, CancellationToken ct)
    {
        var detail = await repository.GetDetailAsync(query.Id, ct);
        return Result.Ok(detail);
    }
}
```

- [ ] **Step 5: Build**

```
dotnet build
```

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.Application/Sessions/Dtos/SessionDetailDto.cs
git add src/AIHelperNET.Application/Sessions/Queries/GetSessionDetailQuery.cs
git add src/AIHelperNET.Application/Abstractions/ISessionRepository.cs
git add src/AIHelperNET.Infrastructure/Persistence/SessionRepository.cs
git commit -m "feat: add GetSessionDetailQuery returning full transcript and answers for history panel"
```

---

### Task 8: IExportService + ExportService (TXT and Markdown)

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IExportService.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/ExportSessionCommand.cs`
- Create: `src/AIHelperNET.Infrastructure/Export/ExportService.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Export/ExportServiceTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// tests/AIHelperNET.Infrastructure.Tests/Export/ExportServiceTests.cs
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Infrastructure.Export;

namespace AIHelperNET.Infrastructure.Tests.Export;

public sealed class ExportServiceTests
{
    private static SessionDetailDto MakeDetail() => new(
        SessionId.New(),
        new DateTimeOffset(2026, 6, 4, 14, 32, 0, TimeSpan.Zero),
        null,
        "AudioAndScreen",
        [new("Other", "Can you explain CQRS?", DateTimeOffset.UtcNow)],
        [new("Can you explain CQRS?", "CQRS separates reads and writes.", DateTimeOffset.UtcNow)]);

    [Fact]
    public void ToTxt_ContainsSpeakerAndText()
    {
        var svc    = new ExportService();
        var detail = MakeDetail();

        var txt = svc.ToTxt(detail);

        Assert.Contains("Other:", txt);
        Assert.Contains("Can you explain CQRS?", txt);
        Assert.Contains("CQRS separates reads and writes.", txt);
    }

    [Fact]
    public void ToMarkdown_ContainsHeadingAndBlockquote()
    {
        var svc    = new ExportService();
        var detail = MakeDetail();

        var md = svc.ToMarkdown(detail);

        Assert.Contains("## Session", md);
        Assert.Contains("> Other:", md);
        Assert.Contains("### Q:", md);
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~ExportServiceTests"
```

- [ ] **Step 3: Create IExportService port**

```csharp
// src/AIHelperNET.Application/Abstractions/IExportService.cs
using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Converts session detail into exportable text formats.</summary>
public interface IExportService
{
    string ToTxt(SessionDetailDto detail);
    string ToMarkdown(SessionDetailDto detail);
}
```

- [ ] **Step 4: Create ExportService**

```csharp
// src/AIHelperNET.Infrastructure/Export/ExportService.cs
using System.Globalization;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Infrastructure.Export;

public sealed class ExportService : IExportService
{
    public string ToTxt(SessionDetailDto d)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Session: {d.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm} | Mode: {d.Mode}");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine();
        sb.AppendLine("TRANSCRIPT");
        foreach (var item in d.Transcript)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"[{item.Timestamp.ToLocalTime():HH:mm:ss}] {item.Speaker}: {item.Text}");
        sb.AppendLine();
        sb.AppendLine("ANSWERS");
        foreach (var a in d.Answers)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Q: {a.QuestionText}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"A: {a.AnswerText}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string ToMarkdown(SessionDetailDto d)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"## Session — {d.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Mode:** {d.Mode}");
        sb.AppendLine();
        sb.AppendLine("### Transcript");
        sb.AppendLine();
        foreach (var item in d.Transcript)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"> {item.Speaker}: {item.Text}  ");
        sb.AppendLine();
        sb.AppendLine("### Answers");
        foreach (var a in d.Answers)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### Q: {a.QuestionText}");
            sb.AppendLine();
            sb.AppendLine(a.AnswerText);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 5: Create ExportSessionCommand**

```csharp
// src/AIHelperNET.Application/Sessions/Commands/ExportSessionCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

public enum ExportFormat { Txt, Markdown }

public sealed record ExportSessionCommand(
    SessionId SessionId,
    ExportFormat Format,
    string OutputPath) : IRequest<Result>;

public sealed class ExportSessionHandler(
    ISessionRepository repository,
    IExportService exportService) : IRequestHandler<ExportSessionCommand, Result>
{
    public async ValueTask<Result> Handle(ExportSessionCommand cmd, CancellationToken ct)
    {
        var detail = await repository.GetDetailAsync(cmd.SessionId, ct);
        if (detail is null) return Result.Fail("Session not found.");

        var content = cmd.Format == ExportFormat.Markdown
            ? exportService.ToMarkdown(detail)
            : exportService.ToTxt(detail);

        await File.WriteAllTextAsync(cmd.OutputPath, content, ct);
        return Result.Ok();
    }
}
```

- [ ] **Step 6: Register IExportService in DI**

In `src/AIHelperNET.Infrastructure/DependencyInjection.cs` (or wherever infrastructure services are registered), add:

```csharp
services.AddSingleton<IExportService, ExportService>();
```

- [ ] **Step 7: Run tests**

```
dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~ExportServiceTests"
```

Expected: all pass.

- [ ] **Step 8: Build**

```
dotnet build
```

- [ ] **Step 9: Commit**

```
git add src/AIHelperNET.Application/Abstractions/IExportService.cs
git add src/AIHelperNET.Application/Sessions/Commands/ExportSessionCommand.cs
git add src/AIHelperNET.Infrastructure/Export/ExportService.cs
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs
git add tests/AIHelperNET.Infrastructure.Tests/Export/ExportServiceTests.cs
git commit -m "feat: add ExportService with TXT/Markdown output and ExportSessionCommand"
```

---

### Task 9: HistoryViewModel

**Files:**
- Create: `src/AIHelperNET.App/ViewModels/HistoryViewModel.cs`

- [ ] **Step 1: Create HistoryViewModel**

```csharp
// src/AIHelperNET.App/ViewModels/HistoryViewModel.cs
using System.Collections.ObjectModel;
using System.Windows;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Ids;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using Microsoft.Win32;

namespace AIHelperNET.App.ViewModels;

public sealed class SessionSummaryVm(SessionSummaryDto dto)
{
    public SessionId         Id            => dto.Id;
    public string            DateLabel     => dto.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string            Mode          => dto.State.ToString();
    public int               QuestionCount => dto.QuestionCount;
    public int               AnswerCount   => dto.AnswerCount;
    public bool              IsActive      => dto.EndedAt is null;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set { _isExpanded = value; OnIsExpandedChanged?.Invoke(); }
    }
    public event Action? OnIsExpandedChanged;

    public SessionDetailDto? Detail { get; set; }
}

public sealed partial class HistoryViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<SessionSummaryVm> Sessions { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        var result = await mediator.Send(new GetSessionHistoryQuery(50));
        if (!result.IsSuccess) return;
        Sessions.Clear();
        foreach (var dto in result.Value)
            Sessions.Add(new SessionSummaryVm(dto));
    }

    [RelayCommand]
    public async Task ToggleExpandAsync(SessionSummaryVm? vm)
    {
        if (vm is null) return;
        if (!vm.IsExpanded)
        {
            if (vm.Detail is null)
            {
                var result = await mediator.Send(new GetSessionDetailQuery(vm.Id));
                if (result.IsSuccess) vm.Detail = result.Value;
            }
            vm.IsExpanded = true;
        }
        else
        {
            vm.IsExpanded = false;
        }
        OnPropertyChanged(nameof(Sessions));
    }

    [RelayCommand]
    public async Task ExportAsync(SessionSummaryVm? vm)
    {
        if (vm is null) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Export session",
            Filter     = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName   = $"session-{vm.DateLabel.Replace(":", "-")}",
            DefaultExt = ".md"
        };

        if (dlg.ShowDialog() != true) return;

        var format = dlg.FilterIndex == 2 ? ExportFormat.Txt : ExportFormat.Markdown;
        var result = await mediator.Send(new ExportSessionCommand(vm.Id, format, dlg.FileName));
        StatusMessage = result.IsSuccess ? "Exported ✓" : $"Error: {string.Join(", ", result.Errors)}";
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build
```

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.App/ViewModels/HistoryViewModel.cs
git commit -m "feat: add HistoryViewModel with load, expand, and export commands"
```

---

### Task 10: HistoryPanel UserControl and MainOverlayWindow integration

**Files:**
- Create: `src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml`
- Create: `src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs`

- [ ] **Step 1: Create HistoryPanel.xaml**

```xml
<!-- src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml -->
<UserControl x:Class="AIHelperNET.App.Windows.Controls.HistoryPanel"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <DockPanel>
        <!-- Header -->
        <DockPanel DockPanel.Dock="Top" Margin="8,6">
            <Button Content="Export…" DockPanel.Dock="Right"
                    Style="{StaticResource ActionBtn}"
                    Command="{Binding ExportCommand}"
                    CommandParameter="{Binding SelectedSession}"/>
            <TextBlock Text="History"
                       FontWeight="SemiBold"
                       FontSize="{DynamicResource Font.SM}"
                       Foreground="{DynamicResource Brush.Foreground.Primary}"
                       VerticalAlignment="Center"/>
        </DockPanel>

        <!-- Search -->
        <TextBox DockPanel.Dock="Top"
                 Margin="8,0,8,6"
                 Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}"
                 PlaceholderText="Search sessions…"/>

        <!-- Status -->
        <TextBlock DockPanel.Dock="Bottom"
                   Text="{Binding StatusMessage}"
                   FontSize="{DynamicResource Font.XS}"
                   Foreground="{DynamicResource Brush.Semantic.Active}"
                   Margin="8,4"/>

        <!-- Session list -->
        <ScrollViewer VerticalScrollBarVisibility="Auto">
            <ItemsControl ItemsSource="{Binding Sessions}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Margin="8,0,8,4"
                                Background="{DynamicResource Brush.Background.Card}"
                                BorderBrush="{DynamicResource Brush.Border}"
                                BorderThickness="1" CornerRadius="4">
                            <StackPanel>
                                <!-- Summary row -->
                                <Button Style="{StaticResource {x:Type Button}}"
                                        Background="Transparent" BorderThickness="0"
                                        Command="{Binding DataContext.ToggleExpandCommand,
                                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                                        CommandParameter="{Binding}">
                                    <Grid Margin="8,6">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBlock Grid.Column="0" Text="{Binding DateLabel}"
                                                   Foreground="{DynamicResource Brush.Foreground.Primary}"
                                                   FontSize="{DynamicResource Font.SM}"/>
                                        <TextBlock Grid.Column="1" Text="{Binding Mode}"
                                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                                   FontSize="{DynamicResource Font.XS}" Margin="8,0"/>
                                        <TextBlock Grid.Column="2"
                                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                                   FontSize="{DynamicResource Font.XS}" Margin="4,0">
                                            <Run Text="{Binding QuestionCount}"/>
                                            <Run Text=" Q"/>
                                        </TextBlock>
                                        <TextBlock Grid.Column="3" Text="▶"
                                                   Foreground="{DynamicResource Brush.Foreground.Muted}"
                                                   FontSize="{DynamicResource Font.XS}"/>
                                    </Grid>
                                </Button>

                                <!-- Expanded detail -->
                                <Border Visibility="{Binding IsExpanded,
                                            Converter={StaticResource BoolToVisibilityConverter}}"
                                        BorderBrush="{DynamicResource Brush.Border}"
                                        BorderThickness="0,1,0,0"
                                        Padding="8,6">
                                    <StackPanel>
                                        <TextBlock Text="TRANSCRIPT"
                                                   Style="{StaticResource SectionLabel}"
                                                   Margin="0,0,0,4"/>
                                        <ItemsControl ItemsSource="{Binding Detail.Transcript}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <TextBlock FontSize="{DynamicResource Font.XS}"
                                                               Foreground="{DynamicResource Brush.Foreground.Secondary}"
                                                               TextWrapping="Wrap" Margin="0,1">
                                                        <Run Text="{Binding Speaker}" FontWeight="SemiBold"/>
                                                        <Run Text=": "/>
                                                        <Run Text="{Binding Text}"/>
                                                    </TextBlock>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                        <TextBlock Text="ANSWERS"
                                                   Style="{StaticResource SectionLabel}"
                                                   Margin="0,8,0,4"/>
                                        <ItemsControl ItemsSource="{Binding Detail.Answers}">
                                            <ItemsControl.ItemTemplate>
                                                <DataTemplate>
                                                    <StackPanel Margin="0,0,0,6">
                                                        <TextBlock Text="{Binding QuestionText}"
                                                                   FontSize="{DynamicResource Font.XS}"
                                                                   FontWeight="SemiBold"
                                                                   Foreground="{DynamicResource Brush.Semantic.Question}"
                                                                   TextWrapping="Wrap"/>
                                                        <TextBlock Text="{Binding AnswerText}"
                                                                   FontSize="{DynamicResource Font.XS}"
                                                                   Foreground="{DynamicResource Brush.Foreground.Secondary}"
                                                                   TextWrapping="Wrap"/>
                                                    </StackPanel>
                                                </DataTemplate>
                                            </ItemsControl.ItemTemplate>
                                        </ItemsControl>
                                    </StackPanel>
                                </Border>
                            </StackPanel>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create HistoryPanel.xaml.cs**

```csharp
// src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml.cs
using System.Windows.Controls;

namespace AIHelperNET.App.Windows.Controls;

public partial class HistoryPanel : UserControl
{
    public HistoryPanel() => InitializeComponent();
}
```

- [ ] **Step 3: Add History button to MainOverlayWindow title bar**

In `MainOverlayWindow.xaml`, in the title bar `StackPanel` (Grid.Column="2"), add a History button before the ⚙ button:

```xml
<Button Content="📋"
        Style="{StaticResource IconBtn}"
        FontSize="12"
        Click="ToggleHistory_Click"
        ToolTip="Session history"/>
```

- [ ] **Step 4: Add HistoryPanel to MainOverlayWindow main content area**

In `MainOverlayWindow.xaml`, wrap the existing transcript+answers Grid in a `Grid` with two children — the live content and the history panel, one visible at a time:

```xml
<!-- Main content area — replace existing <Grid> with: -->
<Grid x:Name="ContentArea">
    <!-- Live session view (existing transcript + splitter + answers Grid) -->
    <Grid x:Name="LiveView">
        <!-- ... existing Grid content unchanged ... -->
    </Grid>

    <!-- History panel -->
    <local:HistoryPanel x:Name="HistoryPanelControl"
                        Visibility="Collapsed"/>
</Grid>
```

Add `xmlns:local="clr-namespace:AIHelperNET.App.Windows.Controls"` to the Window declaration.

- [ ] **Step 5: Update MainOverlayWindowContext and code-behind**

In `MainOverlayWindow.xaml.cs`, add `HistoryViewModel` as a field and wire the toggle:

```csharp
private readonly HistoryViewModel _historyVm;
private bool _showingHistory;

public MainOverlayWindow(
    MainOverlayWindowContext context,
    SettingsWindow settingsWindow,
    SettingsViewModel settingsVm,
    HistoryViewModel historyVm)
{
    InitializeComponent();
    DataContext      = context;
    _settingsWindow  = settingsWindow;
    _settingsVm      = settingsVm;
    _historyVm       = historyVm;
    _settingsVm.OpacityChanged += opacity => Opacity = opacity;
    HistoryPanelControl.DataContext = _historyVm;
}

private async void ToggleHistory_Click(object sender, RoutedEventArgs e)
{
    _showingHistory = !_showingHistory;
    LiveView.Visibility             = _showingHistory ? Visibility.Collapsed : Visibility.Visible;
    HistoryPanelControl.Visibility  = _showingHistory ? Visibility.Visible   : Visibility.Collapsed;
    if (_showingHistory)
        await _historyVm.LoadAsync();
}
```

- [ ] **Step 6: Register HistoryViewModel in DI**

In the service registration (App.xaml.cs or ServiceCollectionExtensions), add:

```csharp
services.AddSingleton<HistoryViewModel>();
```

- [ ] **Step 7: Build and run**

```
dotnet build
dotnet run --project src/AIHelperNET.App
```

Click 📋 → verify History panel loads; start a session, stop it, reopen history — session should appear; click Export and verify a `.md` file is created.

- [ ] **Step 8: Commit**

```
git add src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml
git add src/AIHelperNET.App/Windows/Controls/HistoryPanel.xaml.cs
git add src/AIHelperNET.App/ViewModels/HistoryViewModel.cs
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml.cs
git commit -m "feat: add history panel with session browser and TXT/Markdown export"
```

---

## GROUP 3 — Overlay & Interaction

---

### Task 11: AnswerVersionType.FollowUp and GenerateFollowUpCommand

**Files:**
- Modify: `src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs`
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Create: `src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs`

- [ ] **Step 1: Add FollowUp to AnswerVersionType**

```csharp
// src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs
public enum AnswerVersionType
{
    Preliminary,
    RefinedAfterClarification,
    UpdatedWithScreen,
    ManuallyRegenerated,
    /// <summary>Continuation of a previous answer with user-supplied follow-up text.</summary>
    FollowUp
}
```

- [ ] **Step 2: Add BuildFollowUp to PromptBuilderService**

In `PromptBuilderService.cs`, add a new static method after `Build`:

```csharp
/// <summary>Builds a follow-up prompt with the original Q+A injected as context.</summary>
public static AnswerPrompt BuildFollowUp(
    CodeProfile profile,
    AnswerSettings settings,
    string originalQuestion,
    string previousAnswer,
    string followUpText)
{
    var system = new StringBuilder();
    system.AppendLine(
        "You are a senior software engineer coaching a candidate through a technical job interview. " +
        "You previously answered a question. Now the candidate asks a follow-up. " +
        "Be concise — 2–4 sentences or bullets. No restating the prior answer.");
    AppendCodeProfile(system, profile);

    var user = new StringBuilder();
    user.AppendLine(CultureInfo.InvariantCulture, $"Original question: {originalQuestion}");
    user.AppendLine(CultureInfo.InvariantCulture, $"Your previous answer: {previousAnswer}");
    user.AppendLine(CultureInfo.InvariantCulture, $"Follow-up: {followUpText}");

    return new AnswerPrompt(
        System: system.ToString(),
        User: user.ToString(),
        OutputLanguage: settings.OutputLanguage,
        MaxTokens: MapLengthToTokens(settings.Length));
}
```

- [ ] **Step 3: Write failing test for BuildFollowUp**

```csharp
// tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Tests.Answers;

public sealed class PromptBuilderServiceTests
{
    [Fact]
    public void BuildFollowUp_InjectsOriginalQAndA_InUserPrompt()
    {
        var prompt = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty,
            AnswerSettings.Default,
            "What is CQRS?",
            "CQRS separates reads and writes.",
            "Can you give an example?");

        Assert.Contains("What is CQRS?", prompt.User);
        Assert.Contains("CQRS separates reads and writes.", prompt.User);
        Assert.Contains("Can you give an example?", prompt.User);
    }
}
```

- [ ] **Step 4: Run test — expect pass (code already written)**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~PromptBuilderServiceTests"
```

- [ ] **Step 5: Create GenerateFollowUpCommand**

```csharp
// src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

/// <summary>Generates a follow-up answer using the original question and prior answer as context.</summary>
public sealed record GenerateFollowUpCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string FollowUpText) : IRequest<Result>;

public sealed class GenerateFollowUpHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<GenerateFollowUpCommand, Result>
{
    public async ValueTask<Result> Handle(GenerateFollowUpCommand cmd, CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var question = session.Questions.FirstOrDefault(q => q.Id == turn.InitialQuestionId);
        if (question is null) return Result.Fail("Question not found.");

        // Use the latest answer version text as prior context
        var priorText = turn.AnswerVersions
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefault()?.Text ?? string.Empty;

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = PromptBuilderService.BuildFollowUp(
            session.CodeProfile, session.AnswerSettings,
            question.Text, priorText, cmd.FollowUpText);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, ct))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(cmd.TurnId, AnswerVersionType.FollowUp, chunk, ct);
            }
            answer.Complete(clock.GetUtcNow());
            var version = AnswerVersion.Create(AnswerVersionType.FollowUp, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);
            await streamSink.OnCompleteAsync(cmd.TurnId, AnswerVersionType.FollowUp, ct);
        }
        catch (OperationCanceledException) { answer.Cancel(clock.GetUtcNow()); }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(cmd.TurnId, ex.Message, ct);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

Also update `AnswerVersionVm.VersionLabel` in `ConversationTurnViewModel.cs` to handle the new enum value:

```csharp
AnswerVersionType.FollowUp => "Follow-up",
```

- [ ] **Step 6: Build**

```
dotnet build
```

- [ ] **Step 7: Commit**

```
git add src/AIHelperNET.Domain/Sessions/AnswerVersionType.cs
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs
git add src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git add tests/AIHelperNET.Application.Tests/Answers/PromptBuilderServiceTests.cs
git commit -m "feat: add FollowUp answer version type and GenerateFollowUpCommand"
```

---

### Task 12: ConversationTurnViewModel — follow-up UI state

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

- [ ] **Step 1: Add follow-up properties to TurnVm**

In `TurnVm`, add:

```csharp
private string _followUpText = string.Empty;
public string FollowUpText
{
    get => _followUpText;
    set => SetProperty(ref _followUpText, value);
}
```

- [ ] **Step 2: Add follow-up toggle and command to ConversationTurnViewModel**

```csharp
[ObservableProperty] private bool _isFollowUpEnabled;

[RelayCommand]
private async Task SubmitFollowUpAsync(TurnVm? turn)
{
    if (turn is null || ActiveSessionId is not { } sid) return;
    if (string.IsNullOrWhiteSpace(turn.FollowUpText)) return;
    var text = turn.FollowUpText;
    turn.FollowUpText = string.Empty;
    await mediator.Send(new GenerateFollowUpCommand(sid, turn.Id, text));
}
```

- [ ] **Step 3: Add follow-up toggle to sidebar in MainOverlayWindow.xaml**

In the sidebar `StackPanel`, after the STATUS section and before the Hide button, add:

```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="FOLLOW-UPS" Margin="0,10,0,4"/>
<ToggleButton Content="{Binding ConversationTurn.IsFollowUpEnabled,
                  Converter={StaticResource BoolToStringConverter},
                  ConverterParameter='On|Off'}"
              IsChecked="{Binding ConversationTurn.IsFollowUpEnabled}"
              Style="{StaticResource ActionBtn}"/>
```

- [ ] **Step 4: Add follow-up input to each turn card in MainOverlayWindow.xaml**

In the `DataTemplate` for turns, after the action buttons `StackPanel`, add:

```xml
<StackPanel Orientation="Horizontal"
            Margin="0,4,0,0"
            Visibility="{Binding DataContext.ConversationTurn.IsFollowUpEnabled,
                RelativeSource={RelativeSource AncestorType=Window},
                Converter={StaticResource BoolToVisibilityConverter}}">
    <TextBox Text="{Binding FollowUpText, UpdateSourceTrigger=PropertyChanged}"
             Width="160"
             PlaceholderText="Ask a follow-up…"
             FontSize="{DynamicResource Font.XS}">
        <TextBox.InputBindings>
            <KeyBinding Key="Return"
                        Command="{Binding DataContext.ConversationTurn.SubmitFollowUpCommand,
                            RelativeSource={RelativeSource AncestorType=Window}}"
                        CommandParameter="{Binding}"/>
        </TextBox.InputBindings>
    </TextBox>
    <Button Content="→"
            Style="{StaticResource ActionBtn}"
            Command="{Binding DataContext.ConversationTurn.SubmitFollowUpCommand,
                RelativeSource={RelativeSource AncestorType=Window}}"
            CommandParameter="{Binding}"/>
</StackPanel>
```

- [ ] **Step 5: Build and run**

```
dotnet build
dotnet run --project src/AIHelperNET.App
```

Toggle Follow-ups in the sidebar → verify input fields appear on cards; type a follow-up and press Enter → verify a new answer version appears on the card.

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat: add per-turn follow-up question input with session-level toggle"
```

---

### Task 13: Screen analysis modes

**Files:**
- Create: `src/AIHelperNET.Application/Answers/ScreenAnalysisMode.cs`
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

- [ ] **Step 1: Create ScreenAnalysisMode enum**

```csharp
// src/AIHelperNET.Application/Answers/ScreenAnalysisMode.cs
namespace AIHelperNET.Application.Answers;

public enum ScreenAnalysisMode
{
    General,
    SolveCodingTask,
    DebugError,
    ExplainCode,
    SystemDesign
}
```

- [ ] **Step 2: Add BuildWithScreenMode to PromptBuilderService**

Add a new static method:

```csharp
/// <summary>Builds a prompt for screen-based analysis with mode-specific instructions.</summary>
public static AnswerPrompt BuildWithScreenMode(
    CodeProfile profile,
    AnswerSettings settings,
    string screenContext,
    IEnumerable<string> interviewerLines,
    ScreenAnalysisMode mode)
{
    var system = new StringBuilder();
    system.AppendLine(ModeSystemPrompt(mode));
    AppendCodeProfile(system, profile);

    var user = new StringBuilder();
    var lines = interviewerLines.ToList();
    if (lines.Count > 0)
    {
        user.AppendLine("Interviewer context (recent speech):");
        foreach (var line in lines) user.AppendLine(CultureInfo.InvariantCulture, $"- {line}");
        user.AppendLine();
    }
    user.AppendLine("On-screen content (OCR):");
    user.AppendLine(screenContext);

    return new AnswerPrompt(
        System: system.ToString(),
        User: user.ToString(),
        OutputLanguage: settings.OutputLanguage,
        MaxTokens: MapLengthToTokens(settings.Length));
}

private static string ModeSystemPrompt(ScreenAnalysisMode mode) => mode switch
{
    ScreenAnalysisMode.SolveCodingTask =>
        "You are a senior software engineer. Given the coding task shown on screen, provide a complete, " +
        "working solution in the candidate's stack. Include a brief explanation before the code.",
    ScreenAnalysisMode.DebugError =>
        "You are a senior software engineer. Analyze the error or stack trace shown on screen. " +
        "Identify the root cause and provide a clear fix. Be concise.",
    ScreenAnalysisMode.ExplainCode =>
        "You are a senior software engineer. Explain what the code on screen does, its design patterns, " +
        "and any notable decisions. 3–5 sentences, spoken style.",
    ScreenAnalysisMode.SystemDesign =>
        "You are a senior software engineer. Provide a high-level system design approach for the " +
        "requirements shown. Cover components, data flow, and key trade-offs. Be concise.",
    _ =>
        "You are a senior software engineer coaching a candidate through a technical interview. " +
        "Analyze the content on screen and provide a helpful, concise response the candidate can use."
};
```

- [ ] **Step 3: Add screen analysis state to SessionControlViewModel**

```csharp
[ObservableProperty] private ScreenAnalysisMode _screenAnalysisMode = ScreenAnalysisMode.General;
[ObservableProperty] private bool _includeInterviewerContext = true;
```

- [ ] **Step 4: Update CaptureScreenAsync in ConversationTurnViewModel**

Replace the existing `CaptureScreenAsync` method:

```csharp
[RelayCommand]
private async Task CaptureScreenAsync(SessionControlViewModel? sessionControl)
{
    if (ActiveSessionId is not { } sid) return;
    if (sessionControl is null) return;

    var ocrResult = await mediator.Send(new CaptureScreenCommand());
    if (ocrResult.IsFailed) return;

    string[] interviewerLines = [];
    if (sessionControl.IncludeInterviewerContext)
    {
        // Caller must inject transcript items — resolved via parameter below
        interviewerLines = _lastInterviewerLines;
    }

    // Use screen-mode prompt via a dedicated version type (UpdatedWithScreen)
    var get = await mediator.Send(new GetSettingsQuery());
    var settings = get.IsSuccess ? get.Value : null;
    // Pass mode and interviewer lines through a new overload
    if (Turns.FirstOrDefault() is { } activeTurn)
    {
        await mediator.Send(new RegenerateAnswerWithScreenCommand(
            sid, activeTurn.Id, ocrResult.Value,
            sessionControl.ScreenAnalysisMode, interviewerLines));
    }
}

// Injected by TranscriptViewModel when new items arrive
private string[] _lastInterviewerLines = [];

public void UpdateInterviewerLines(IEnumerable<string> lines)
    => _lastInterviewerLines = lines.ToArray();
```

Create the new command:

```csharp
// src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerWithScreenCommand.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Answers.Commands;

public sealed record RegenerateAnswerWithScreenCommand(
    SessionId SessionId,
    ConversationTurnId TurnId,
    string ScreenContext,
    ScreenAnalysisMode Mode,
    string[] InterviewerLines) : IRequest<Result>;

public sealed class RegenerateAnswerWithScreenHandler(
    ISessionRepository repository,
    IAnswerProviderResolver providerResolver,
    ISettingsStore settingsStore,
    IAnswerStreamSink streamSink,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : IRequestHandler<RegenerateAnswerWithScreenCommand, Result>
{
    public async ValueTask<Result> Handle(RegenerateAnswerWithScreenCommand cmd, CancellationToken ct)
    {
        var settings = await settingsStore.LoadAsync(ct);
        var provider = providerResolver.Resolve(settings.ActiveBackend);

        var get = await repository.GetAsync(cmd.SessionId, ct);
        if (get.IsFailed) return get.ToResult();
        var session = get.Value;

        var turn = session.ConversationTurns.FirstOrDefault(t => t.Id == cmd.TurnId);
        if (turn is null) return Result.Fail("ConversationTurn not found.");

        var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
        if (start.IsFailed) return Result.Fail(start.Error);
        var answer = start.Value;

        var prompt = PromptBuilderService.BuildWithScreenMode(
            session.CodeProfile, session.AnswerSettings,
            cmd.ScreenContext, cmd.InterviewerLines, cmd.Mode);

        var chunks = new System.Text.StringBuilder();
        try
        {
            await foreach (var chunk in provider.StreamAnswerAsync(prompt, ct))
            {
                answer.AppendChunk(chunk);
                chunks.Append(chunk);
                await streamSink.OnChunkAsync(cmd.TurnId, AnswerVersionType.UpdatedWithScreen, chunk, ct);
            }
            answer.Complete(clock.GetUtcNow());
            var version = AnswerVersion.Create(AnswerVersionType.UpdatedWithScreen, chunks.ToString(), clock.GetUtcNow());
            turn.AddAnswerVersion(version);
            await streamSink.OnCompleteAsync(cmd.TurnId, AnswerVersionType.UpdatedWithScreen, ct);
        }
        catch (OperationCanceledException) { answer.Cancel(clock.GetUtcNow()); }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            answer.Fail(clock.GetUtcNow());
            await streamSink.OnErrorAsync(cmd.TurnId, ex.Message, ct);
        }
#pragma warning restore CA1031

        repository.Update(session);
        return await unitOfWork.SaveChangesAsync(ct);
    }
}
```

- [ ] **Step 5: Wire UpdateInterviewerLines from TranscriptViewModel**

In `TranscriptViewModel.AddItem`, after adding the item call into `ConversationTurnViewModel`:

```csharp
// In App.OnStartup or wherever sinks are wired, also update interviewer lines on each transcript add:
// transcriptSink.SetHandler(item => {
//     transcript.AddItem(item);
//     if (item.Speaker == Speaker.Other)
//     {
//         var last5 = transcript.Items
//             .Where(i => i.Speaker == Speaker.Other)
//             .TakeLast(5)
//             .Select(i => i.Text);
//         conversationTurn.UpdateInterviewerLines(last5);
//     }
// });
```

Add this logic where the transcript sink handler is wired in `App.OnStartup`.

- [ ] **Step 6: Add Screen Analysis section to sidebar in MainOverlayWindow.xaml**

In the sidebar `StackPanel`, after the FOLLOW-UPS section, add:

```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="SCREEN" Margin="0,10,0,4"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="General"
             GroupName="ScreenMode"
             IsChecked="{Binding SessionControl.ScreenAnalysisMode,
                 Converter={StaticResource EnumToBoolConverter},
                 ConverterParameter=General}"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="Solve coding"
             GroupName="ScreenMode"
             IsChecked="{Binding SessionControl.ScreenAnalysisMode,
                 Converter={StaticResource EnumToBoolConverter},
                 ConverterParameter=SolveCodingTask}"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="Debug error"
             GroupName="ScreenMode"
             IsChecked="{Binding SessionControl.ScreenAnalysisMode,
                 Converter={StaticResource EnumToBoolConverter},
                 ConverterParameter=DebugError}"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="Explain code"
             GroupName="ScreenMode"
             IsChecked="{Binding SessionControl.ScreenAnalysisMode,
                 Converter={StaticResource EnumToBoolConverter},
                 ConverterParameter=ExplainCode}"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="System design"
             GroupName="ScreenMode"
             IsChecked="{Binding SessionControl.ScreenAnalysisMode,
                 Converter={StaticResource EnumToBoolConverter},
                 ConverterParameter=SystemDesign}"/>
<CheckBox Content="+ interviewer (5 lines)"
          IsChecked="{Binding SessionControl.IncludeInterviewerContext}"
          Foreground="{DynamicResource Brush.Foreground.Secondary}"
          FontSize="{DynamicResource Font.XS}"
          Margin="0,4,0,0"/>
<Button Content="📷 Capture"
        Style="{StaticResource ActionBtn}"
        Margin="0,4,0,0"
        Command="{Binding ConversationTurn.CaptureScreenCommand}"
        CommandParameter="{Binding SessionControl}"/>
```

Update `EnumToBoolConverter` to handle `ScreenAnalysisMode` (it likely already works for any enum via `ConverterParameter` string comparison — verify in the converter source).

- [ ] **Step 7: Build and run**

```
dotnet build
dotnet run --project src/AIHelperNET.App
```

Select "Solve coding" mode, check "+ interviewer", press 📷 Capture — verify OCR runs and answer is generated with coding-task prompt style.

- [ ] **Step 8: Commit**

```
git add src/AIHelperNET.Application/Answers/ScreenAnalysisMode.cs
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs
git add src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerWithScreenCommand.cs
git add src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git commit -m "feat: add screen analysis modes (General/SolveCoding/Debug/Explain/Design) with interviewer context"
```

---

### Task 14: Audio level meters

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IAudioLevelMonitor.cs`
- Create: `src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs`
- Create: `src/AIHelperNET.App/ViewModels/AudioLevelViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs`

- [ ] **Step 1: Create IAudioLevelMonitor**

```csharp
// src/AIHelperNET.Application/Abstractions/IAudioLevelMonitor.cs
namespace AIHelperNET.Application.Abstractions;

/// <summary>Monitors real-time audio peak levels for mic and loopback independently of session state.</summary>
public interface IAudioLevelMonitor
{
    event Action<double> MicLevelChanged;
    event Action<double> SystemLevelChanged;

    /// <summary>Start level monitoring using the configured device IDs.</summary>
    Task StartAsync(string? micDeviceId, string? loopbackDeviceId, CancellationToken ct);

    /// <summary>Stop monitoring and release devices.</summary>
    Task StopAsync();
}
```

- [ ] **Step 2: Create AudioLevelMonitor**

```csharp
// src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs
using AIHelperNET.Application.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class AudioLevelMonitor : IAudioLevelMonitor, IAsyncDisposable
{
    public event Action<double>? MicLevelChanged;
    public event Action<double>? SystemLevelChanged;

    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loopback;
    private CancellationTokenSource? _cts;

    public Task StartAsync(string? micDeviceId, string? loopbackDeviceId, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var enumerator = new MMDeviceEnumerator();

        var micDevice = micDeviceId is not null
            ? enumerator.GetDevice(micDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        var loopbackDevice = loopbackDeviceId is not null
            ? enumerator.GetDevice(loopbackDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        _mic = new WasapiCapture(micDevice);
        _mic.DataAvailable += (_, e) =>
        {
            var peak = ComputePeak(e.Buffer, e.BytesRecorded, _mic.WaveFormat);
            MicLevelChanged?.Invoke(peak);
        };

        _loopback = new WasapiLoopbackCapture(loopbackDevice);
        _loopback.DataAvailable += (_, e) =>
        {
            var peak = ComputePeak(e.Buffer, e.BytesRecorded, _loopback.WaveFormat);
            SystemLevelChanged?.Invoke(peak);
        };

        try
        {
            _mic.StartRecording();
            _loopback.StartRecording();
            Log.Debug("AudioLevelMonitor: started");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioLevelMonitor: failed to start");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts?.Cancel();
        _mic?.StopRecording();
        _loopback?.StopRecording();
        _mic?.Dispose();
        _loopback?.Dispose();
        _mic      = null;
        _loopback = null;
        Log.Debug("AudioLevelMonitor: stopped");
        return Task.CompletedTask;
    }

    private static double ComputePeak(byte[] buffer, int bytesRecorded, WaveFormat fmt)
    {
        if (bytesRecorded == 0 || fmt.BitsPerSample != 16) return 0;
        double peak = 0;
        for (var i = 0; i < bytesRecorded - 1; i += 2)
        {
            var sample = Math.Abs(BitConverter.ToInt16(buffer, i) / 32768.0);
            if (sample > peak) peak = sample;
        }
        return peak;
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
```

- [ ] **Step 3: Create AudioLevelViewModel**

```csharp
// src/AIHelperNET.App/ViewModels/AudioLevelViewModel.cs
using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

public sealed partial class AudioLevelViewModel(IAudioLevelMonitor monitor) : ObservableObject
{
    [ObservableProperty] private double _micLevel;
    [ObservableProperty] private double _systemLevel;

    public void Subscribe()
    {
        monitor.MicLevelChanged    += level => System.Windows.Application.Current.Dispatcher
            .BeginInvoke(() => MicLevel    = level);
        monitor.SystemLevelChanged += level => System.Windows.Application.Current.Dispatcher
            .BeginInvoke(() => SystemLevel = level);
    }
}
```

- [ ] **Step 4: Register and start in App.xaml.cs**

In DI registration, add:

```csharp
services.AddSingleton<IAudioLevelMonitor, AudioLevelMonitor>();
services.AddSingleton<AudioLevelViewModel>();
```

In `App.OnStartup`, after the host starts and before `overlay.Show()`, start monitoring:

```csharp
var monitor  = host.Services.GetRequiredService<IAudioLevelMonitor>();
var settings = await host.Services.GetRequiredService<ISettingsStore>()
    .LoadAsync(CancellationToken.None);
await monitor.StartAsync(settings.MicDeviceId, settings.LoopbackDeviceId, CancellationToken.None);

var levelVm = host.Services.GetRequiredService<AudioLevelViewModel>();
levelVm.Subscribe();
```

On `App.OnExit`, stop monitoring:

```csharp
var monitor = host.Services.GetRequiredService<IAudioLevelMonitor>();
await monitor.StopAsync();
```

- [ ] **Step 5: Add AudioLevelViewModel to MainOverlayWindowContext**

In `MainOverlayWindowContext`:

```csharp
public sealed class MainOverlayWindowContext(
    SessionControlViewModel sessionControl,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn,
    AudioLevelViewModel audioLevel)
{
    public SessionControlViewModel  SessionControl  => sessionControl;
    public TranscriptViewModel      Transcript      => transcript;
    public ConversationTurnViewModel ConversationTurn => conversationTurn;
    public AudioLevelViewModel      AudioLevel      => audioLevel;
}
```

Update the DI registration of `MainOverlayWindowContext` to inject `AudioLevelViewModel`.

- [ ] **Step 6: Add audio level meters to sidebar in MainOverlayWindow.xaml**

In the sidebar `StackPanel`, after the STATUS section, add:

```xml
<TextBlock Style="{StaticResource SectionLabel}" Text="AUDIO LEVELS" Margin="0,10,0,4"/>
<TextBlock Text="🎤" FontSize="{DynamicResource Font.XS}"
           Foreground="{DynamicResource Brush.Foreground.Muted}"/>
<ProgressBar Value="{Binding AudioLevel.MicLevel}"
             Minimum="0" Maximum="1"
             Height="6" Margin="0,1,0,4"
             Background="{DynamicResource Brush.Background.Panel}"
             Foreground="{DynamicResource Brush.Semantic.Active}"/>
<TextBlock Text="🔊" FontSize="{DynamicResource Font.XS}"
           Foreground="{DynamicResource Brush.Foreground.Muted}"/>
<ProgressBar Value="{Binding AudioLevel.SystemLevel}"
             Minimum="0" Maximum="1"
             Height="6" Margin="0,1,0,0"
             Background="{DynamicResource Brush.Background.Panel}"
             Foreground="#44AAFF"/>
```

- [ ] **Step 7: Build and run**

```
dotnet build
dotnet run --project src/AIHelperNET.App
```

Speak into the mic — verify the 🎤 bar animates. Play audio — verify 🔊 bar animates. Both should respond before a session is started.

- [ ] **Step 8: Commit**

```
git add src/AIHelperNET.Application/Abstractions/IAudioLevelMonitor.cs
git add src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs
git add src/AIHelperNET.App/ViewModels/AudioLevelViewModel.cs
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git add src/AIHelperNET.App/Windows/MainOverlayWindowContext.cs
git add src/AIHelperNET.Infrastructure/DependencyInjection.cs
git add src/AIHelperNET.App/App.xaml.cs
git commit -m "feat: add always-on audio level meters for mic and loopback in sidebar"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Task covering it |
|-----------------|-----------------|
| Audio device selection UI | Task 5 (Audio tab device combos) |
| Whisper language selection | Task 3 (ITranscriptionService) + Task 5 (Audio tab language combo) |
| Code Profile UI | Task 4 (SettingsViewModel) + Task 5 (Code Profiles tab) |
| Named presets (Code Profile + Answer Settings) | Task 1 (ProfilePreset) + Task 4 (preset commands) + Task 5 (XAML) |
| Overlay opacity slider | Task 5 (Appearance tab) + Task 6 (init + live) |
| SaveSettingsCommand | Task 2 |
| Session history browser | Task 7 (detail query) + Task 10 (panel + main window) |
| Session history export (TXT/MD) | Task 8 (ExportService) + Task 10 (Export button) |
| Session pause/resume | Deferred — not in this plan |
| Follow-up questions with session toggle | Task 11 (command) + Task 12 (UI) |
| Screen analysis modes (5 modes) | Task 13 |
| Interviewer context (last 5 lines) | Task 13 |
| Audio level meters (always-on) | Task 14 |
| Compact overlay strip | Deferred — not in this plan |

All spec requirements are covered or explicitly deferred.

**No placeholders found.**

**Type consistency confirmed:** `ProfilePreset` used in Tasks 1, 4, 5 consistently. `SessionDetailDto` used in Tasks 7, 8, 9, 10 consistently. `ScreenAnalysisMode` used in Tasks 13 (enum, PromptBuilderService, ViewModel, XAML) consistently. `IAudioLevelMonitor` used in Tasks 14 consistently. `AudioLevelViewModel` registered in DI and added to `MainOverlayWindowContext` in Task 14.
