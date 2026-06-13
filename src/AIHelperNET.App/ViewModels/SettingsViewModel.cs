// src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
using System.Collections.ObjectModel;
using AIHelperNET.App.Hotkeys;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

/// <summary>Backing ViewModel for the tabs of SettingsWindow.</summary>
public sealed partial class SettingsViewModel(IMediator mediator, IHotkeyApplier hotkeyApplier) : ObservableObject
{
    // ── Shortcuts tab ─────────────────────────────────────────────
    /// <summary>Editable shortcut rows, one per action, shown in the Shortcuts tab.</summary>
    public ObservableCollection<HotkeyRowViewModel> HotkeyRows { get; } = [];

    /// <summary>The last successfully-registered effective set, used to revert on an OS conflict.</summary>
    private IReadOnlyList<HotkeyBinding> _lastGoodBindings = HotkeyDefaults.All;

    // ── API Key tab ───────────────────────────────────────────────
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    // ── AI Backend (API Key tab) ──────────────────────────────────
    [ObservableProperty] private AiBackend _activeBackend = AiBackend.Claude;

    // ── Audio tab ─────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedMicDeviceId;
    [ObservableProperty] private string? _selectedLoopbackDeviceId;
    [ObservableProperty] private string _whisperLanguage = "auto";
    [ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.Medium;

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

    // ── Answer settings ───────────────────────────────────────────
    [ObservableProperty] private int _maxAnswerTokens = 800;
    [ObservableProperty] private int _latestQuestionWindowSeconds = 120;

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
        MaxAnswerTokens               = s.MaxAnswerTokens;
        LatestQuestionWindowSeconds   = s.LatestQuestionWindowSeconds;
        ActiveBackend                 = s.ActiveBackend;

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

        var effective = HotkeyDefaults.Resolve(s.HotkeyOverrides);
        _lastGoodBindings = effective;
        HotkeyRows.Clear();
        foreach (var b in effective) HotkeyRows.Add(HotkeyRowViewModel.FromBinding(b));

        await RefreshKeyStatusAsync();
    }

    // ── API Key commands ──────────────────────────────────────────
    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput)) return;
        using var secure = new System.Security.SecureString();
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

        IReadOnlyList<HotkeyOverride> hotkeyOverridesToSave;
        if (HotkeyRows.Count > 0)
        {
            var proposed = HotkeyRows.Select(r => r.ToBinding()).ToList();

            var errors = HotkeyValidator.Validate(proposed);
            if (errors.Count > 0)
            {
                foreach (var row in HotkeyRows)
                    row.ErrorMessage = errors.TryGetValue(row.Id, out var msg) ? msg : null;
                StatusMessage = "Fix the highlighted shortcut conflicts, then Save.";
                return;
            }
            foreach (var row in HotkeyRows) row.ErrorMessage = null;

            var failed = hotkeyApplier.Apply(proposed);
            if (failed.Count > 0)
            {
                foreach (var row in HotkeyRows)
                    if (failed.Contains(row.Id))
                        row.ErrorMessage = "Already in use by Windows or another app — pick a different chord.";
                hotkeyApplier.Apply(_lastGoodBindings); // revert so no action is left unregistered
                StatusMessage = "Some shortcuts are in use by another app — not saved.";
                return;
            }

            _lastGoodBindings = proposed;
            var defaults = HotkeyDefaults.All.ToDictionary(b => b.Id);
            hotkeyOverridesToSave = proposed
                .Where(b => b.Modifiers != defaults[b.Id].Modifiers || b.Key != defaults[b.Id].Key)
                .Select(b => new HotkeyOverride(b.Id, b.Modifiers, b.Key))
                .ToList();
        }
        else
        {
            hotkeyOverridesToSave = current?.HotkeyOverrides ?? [];
        }

        var dto = new AppSettingsDto(
            ActiveBackend,
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
            OverlayOpacity,
            MaxAnswerTokens,
            LatestQuestionWindowSeconds)
        {
            Presets = [.. Presets],
            HotkeyOverrides = [.. hotkeyOverridesToSave]
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

    // ── Shortcut editing ──────────────────────────────────────────
    /// <summary>True while any row is capturing a key press.</summary>
    public bool IsAnyRowRecording => HotkeyRows.Any(r => r.IsRecording);

    [RelayCommand]
    private void StartRecording(HotkeyRowViewModel? row)
    {
        if (row is null) return;
        foreach (var r in HotkeyRows) r.IsRecording = false;
        row.ErrorMessage = null;
        row.IsRecording = true;
    }

    [RelayCommand]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "[RelayCommand] requires an instance method; static methods cannot be decorated.")]
    private void ResetRow(HotkeyRowViewModel? row)
    {
        if (row is null) return;
        var d = HotkeyDefaults.All.Single(b => b.Id == row.Id);
        row.SetChord(d.Modifiers, d.Key);
    }

    [RelayCommand]
    private void ResetAllHotkeys()
    {
        var defaults = HotkeyDefaults.All.ToDictionary(b => b.Id);
        foreach (var row in HotkeyRows) row.SetChord(defaults[row.Id].Modifiers, defaults[row.Id].Key);
    }

    /// <summary>Called by the window's key handler when a complete chord is captured.</summary>
    /// <param name="mods">The captured modifier flags.</param>
    /// <param name="key">The captured key.</param>
    public void ApplyRecordedChord(ModifierKeys mods, VirtualKey key)
    {
        var row = HotkeyRows.FirstOrDefault(r => r.IsRecording);
        if (row is null) return;
        row.SetChord(mods, key);
        row.IsRecording = false;
    }

    /// <summary>Called by the window's key handler when an unsupported key is pressed while recording.</summary>
    /// <param name="message">The error message to show on the recording row.</param>
    public void SetRecordingError(string message)
    {
        var row = HotkeyRows.FirstOrDefault(r => r.IsRecording);
        if (row is not null) row.ErrorMessage = message;
    }
}
