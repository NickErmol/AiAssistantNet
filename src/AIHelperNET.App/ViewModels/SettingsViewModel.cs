// src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
using System.Collections.ObjectModel;
using AIHelperNET.Application.Abstractions;
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
    [ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.Base;

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
        WhisperModel             = s.WhisperModel;
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
            WhisperModel,
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
