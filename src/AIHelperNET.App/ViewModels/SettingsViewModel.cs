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
using Microsoft.Win32;

namespace AIHelperNET.App.ViewModels;

/// <summary>Backing ViewModel for the tabs of SettingsWindow.</summary>
public sealed partial class SettingsViewModel(
    IMediator mediator,
    IHotkeyApplier hotkeyApplier,
    ITranscriptionGlossaryProvider glossary,
    IDocumentTextExtractor documentTextExtractor,
    IProfileCondenser profileCondenser) : ObservableObject
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
    [ObservableProperty] private AnswerModel _answerModel = AnswerModel.Haiku;

    // ── Audio tab ─────────────────────────────────────────────────
    [ObservableProperty] private string? _selectedMicDeviceId;
    [ObservableProperty] private string? _selectedLoopbackDeviceId;
    [ObservableProperty] private string _whisperLanguage = "auto";
    [ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.LargeTurbo;

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

    // ── Profile tab ───────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CondenseCommand))]
    private string _resumeRawText = string.Empty;

    [ObservableProperty] private string _jobDescriptionRawText = string.Empty;
    [ObservableProperty] private string _candidateProfileCard = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CondenseCommand))]
    private bool _isCondensing;

    [ObservableProperty] private string _profileErrorMessage = string.Empty;

    /// <summary>
    /// Injectable file-picker delegate used by the Load-from-file commands.
    /// Default shows a WPF OpenFileDialog; override in tests to inject a path directly.
    /// </summary>
    /// <remarks>
    /// The default implementation shows a WPF <see cref="Microsoft.Win32.OpenFileDialog"/>
    /// and must run on the WPF UI thread (STA). Commands call <see cref="PickFile"/> before
    /// any <see langword="await"/> to ensure this contract is met.
    /// </remarks>
    internal Func<string?> PickFile { get; set; } = () =>
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Documents|*.pdf;*.docx;*.txt;*.md|All files|*.*",
            Title  = "Select a document"
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    };

    private bool CanCondense => !IsCondensing && !string.IsNullOrWhiteSpace(ResumeRawText);

    [RelayCommand]
    private async Task LoadResumeFromFileAsync(CancellationToken ct)
    {
        var path = PickFile();
        if (path is null) return;

        var result = await documentTextExtractor.ExtractAsync(path, ct);
        if (result.IsSuccess)
        {
            ResumeRawText = result.Value;
            ProfileErrorMessage = string.Empty;
        }
        else
        {
            ProfileErrorMessage = string.Join("; ", result.Errors.Select(e => e.Message));
        }
    }

    [RelayCommand]
    private async Task LoadJobDescriptionFromFileAsync(CancellationToken ct)
    {
        var path = PickFile();
        if (path is null) return;

        var result = await documentTextExtractor.ExtractAsync(path, ct);
        if (result.IsSuccess)
        {
            JobDescriptionRawText = result.Value;
            ProfileErrorMessage = string.Empty;
        }
        else
        {
            ProfileErrorMessage = string.Join("; ", result.Errors.Select(e => e.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(CanCondense))]
    private async Task CondenseAsync(CancellationToken ct)
    {
        if (IsCondensing) return;
        IsCondensing = true;
        try
        {
            var jd = string.IsNullOrWhiteSpace(JobDescriptionRawText) ? null : JobDescriptionRawText;
            var result = await profileCondenser.CondenseAsync(ResumeRawText, jd, ct);
            if (result.IsSuccess)
            {
                CandidateProfileCard = result.Value;
                ProfileErrorMessage  = string.Empty;
            }
            else
            {
                ProfileErrorMessage = string.Join("; ", result.Errors.Select(e => e.Message));
            }
        }
        catch (OperationCanceledException)
        {
            // Silent — user cancelled or app is shutting down.
        }
        catch (Exception ex)
        {
            ProfileErrorMessage = ex.Message;
        }
        finally
        {
            IsCondensing = false;
        }
    }

    // ── Appearance tab ────────────────────────────────────────────
    [ObservableProperty] private double _overlayOpacity = 0.75;
    [ObservableProperty] private OverlayMode _overlayMode = OverlayMode.Stealth;

    /// <summary>The overlay mode last loaded/saved, used to detect a change that needs an app restart.</summary>
    private OverlayMode _loadedOverlayMode = OverlayMode.Stealth;

    /// <summary>Raised after Save when <see cref="OverlayMode"/> changed and the app must restart to apply it.</summary>
    public event Action? OverlayModeChangeRequiresRestart;

    /// <summary>True when the chosen mode is See-through (real translucency, no stealth).</summary>
    public bool IsSeeThroughMode => OverlayMode == OverlayMode.SeeThrough;

    partial void OnOverlayModeChanged(OverlayMode value) => OnPropertyChanged(nameof(IsSeeThroughMode));

    /// <summary>Gets the overlay see-through amount (1 − opacity); drives the Transparency readout.</summary>
    public double OverlayTransparency => 1.0 - OverlayOpacity;

    /// <summary>Raised when opacity changes so MainOverlayWindow can update live.</summary>
    public event Action<double>? OpacityChanged;

    partial void OnOverlayOpacityChanged(double value)
    {
        OpacityChanged?.Invoke(value);
        OnPropertyChanged(nameof(OverlayTransparency));
    }

    // ── Answer settings ───────────────────────────────────────────
    [ObservableProperty] private int _maxAnswerTokens = 800;
    [ObservableProperty] private int _latestQuestionWindowSeconds = 120;

    // ── Glossary (Audio tab) ──────────────────────────────────────
    /// <summary>Whether the transcription glossary biases speech-to-text.</summary>
    [ObservableProperty] private bool _glossaryEnabled = true;

    /// <summary>One checkbox row per glossary domain.</summary>
    public ObservableCollection<GlossaryDomainToggle> GlossaryDomains { get; } = [];

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
        OverlayMode              = s.OverlayMode;
        _loadedOverlayMode       = s.OverlayMode;
        MaxAnswerTokens               = s.MaxAnswerTokens;
        LatestQuestionWindowSeconds   = s.LatestQuestionWindowSeconds;
        ActiveBackend                 = s.ActiveBackend;
        AnswerModel                   = s.AnswerModel;

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

        ResumeRawText       = s.ResumeRawText       ?? string.Empty;
        JobDescriptionRawText = s.JobDescriptionRawText ?? string.Empty;
        CandidateProfileCard  = s.CandidateProfileCard  ?? string.Empty;

        Presets.Clear();
        foreach (var p in s.Presets) Presets.Add(p);

        var effective = HotkeyDefaults.Resolve(s.HotkeyOverrides);
        _lastGoodBindings = effective;
        HotkeyRows.Clear();
        foreach (var b in effective) HotkeyRows.Add(HotkeyRowViewModel.FromBinding(b));

        GlossaryEnabled = s.GlossaryEnabled;
        GlossaryDomains.Clear();
        var enabledDomains = new HashSet<string>(s.EnabledGlossaryDomains, StringComparer.OrdinalIgnoreCase);
        foreach (var d in glossary.Domains)
            GlossaryDomains.Add(new GlossaryDomainToggle(d.Key, d.DisplayName, enabledDomains.Contains(d.Key)));

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
        var result = await mediator.Send(new SaveApiKeyCommand(SecretKind.Anthropic, secure));
        StatusMessage = result.IsSuccess ? "API key saved ✓" : $"Error: {string.Join(", ", result.Errors)}";
        ApiKeyInput   = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteApiKeyAsync()
    {
        var result = await mediator.Send(new DeleteApiKeyCommand(SecretKind.Anthropic));
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
                var revertFailed = hotkeyApplier.Apply(_lastGoodBindings); // revert so no action is left unregistered
                StatusMessage = revertFailed.Count > 0
                    ? "Some shortcuts are in use by another app — not saved, and some may be inactive until restart."
                    : "Some shortcuts are in use by another app — not saved.";
                return;
            }

            _lastGoodBindings = proposed;
            var defaults = HotkeyDefaults.All.ToDictionary(b => b.Id);
            // proposed rows come from HotkeyDefaults.Resolve, so every b.Id is present in HotkeyDefaults.All.
            hotkeyOverridesToSave = proposed
                .Where(b => b.Modifiers != defaults[b.Id].Modifiers || b.Key != defaults[b.Id].Key)
                .Select(b => new HotkeyOverride(b.Id, b.Modifiers, b.Key))
                .ToList();
        }
        else
        {
            // No rows means Save was called before LoadAsync populated them (not reachable via the UI,
            // which always loads first) — preserve whatever overrides are already persisted.
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
            LatestQuestionWindowSeconds,
            OverlayMode,
            GlossaryEnabled,
            AnswerModel)
        {
            Presets = [.. Presets],
            HotkeyOverrides = [.. hotkeyOverridesToSave],
            EnabledGlossaryDomains = GlossaryDomains.Where(d => d.IsEnabled).Select(d => d.Key).ToList(),
            ResumeRawText       = NullIfEmpty(ResumeRawText),
            JobDescriptionRawText = NullIfEmpty(JobDescriptionRawText),
            CandidateProfileCard  = NullIfEmpty(CandidateProfileCard)
        };

        await mediator.Send(new SaveSettingsCommand(dto));
        StatusMessage = "Settings saved ✓";

        if (OverlayMode != _loadedOverlayMode)
        {
            _loadedOverlayMode = OverlayMode;
            OverlayModeChangeRequiresRestart?.Invoke();
        }
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
        var hasKey = await mediator.Send(new HasApiKeyQuery(SecretKind.Anthropic));
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

    /// <summary>Called by the window's key handler to abort recording (Escape) without changing the chord.</summary>
    public void CancelRecording()
    {
        foreach (var row in HotkeyRows) row.IsRecording = false;
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
