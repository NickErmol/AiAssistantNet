using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

/// <summary>Controls session lifecycle and capture mode selection.</summary>
public sealed partial class SessionControlViewModel(
    IMediator mediator,
    SessionRunner runner,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn) : ObservableObject
{
    /// <summary>Gets or sets a value indicating whether a session is currently active.</summary>
    [ObservableProperty] private bool _isSessionActive;

    /// <summary>Gets or sets a value indicating whether the sidebar panel is visible.</summary>
    [ObservableProperty] private bool _isSidebarVisible = true;

    /// <summary>Gets or sets the current session capture mode.</summary>
    [ObservableProperty] private SessionMode _mode = SessionMode.AudioAndScreen;

    /// <summary>Gets or sets the audio source selection.</summary>
    [ObservableProperty] private AudioSourceMode _audioSource = AudioSourceMode.Both;

    /// <summary>Gets or sets a value indicating whether the microphone is active.</summary>
    [ObservableProperty] private bool _isMicActive;

    /// <summary>Gets or sets a value indicating whether system audio capture is active.</summary>
    [ObservableProperty] private bool _isSystemAudioActive;

    /// <summary>Gets or sets a value indicating whether OCR capture is ready.</summary>
    [ObservableProperty] private bool _isOcrReady = true;

    /// <summary>Gets or sets a value indicating whether the AI backend is connected.</summary>
    [ObservableProperty] private bool _isAiConnected;

    /// <summary>Gets or sets the screen analysis mode for capture-based answer generation.</summary>
    [ObservableProperty] private ScreenAnalysisMode _screenAnalysisMode = ScreenAnalysisMode.General;

    /// <summary>Gets or sets a value indicating whether to include recent interviewer lines in screen prompts.</summary>
    [ObservableProperty] private bool _includeInterviewerContext = true;

    /// <summary>Gets the active session identifier, or <see langword="null"/> when stopped.</summary>
    public SessionId? ActiveSessionId { get; private set; }

    /// <summary>Starts or stops the current session.</summary>
    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsSessionActive)
        {
            var settingsResult = await mediator.Send(new GetSettingsQuery());
            var settings = settingsResult.IsSuccess ? settingsResult.Value : null;

            var result = await mediator.Send(new StartSessionCommand(
                settings?.AnswerSettings ?? AnswerSettings.Default,
                settings?.CodeProfile   ?? CodeProfile.Empty,
                Mode, AudioSource));

            if (result.IsSuccess)
            {
                transcript.Clear();
                conversationTurn.Clear();

                ActiveSessionId                  = result.Value.Id;
                conversationTurn.ActiveSessionId = result.Value.Id;
                IsSessionActive                  = true;
                IsMicActive                      = AudioSource is AudioSourceMode.MicrophoneOnly or AudioSourceMode.Both;
                IsSystemAudioActive              = AudioSource is AudioSourceMode.SystemAudioOnly or AudioSourceMode.Both;

                await runner.StartAsync(
                    result.Value.Id,
                    new AudioDeviceSelection(settings?.MicDeviceId, settings?.LoopbackDeviceId),
                    settings?.WhisperModel ?? WhisperModelSize.Base,
                    settings?.WhisperLanguage ?? "auto",
                    AudioSource);
            }
        }
        else if (ActiveSessionId is { } id)
        {
            await runner.StopAsync();
            await mediator.Send(new StopSessionCommand(id));
            IsSessionActive                  = false;
            IsMicActive                      = false;
            IsSystemAudioActive              = false;
            ActiveSessionId                  = null;
            conversationTurn.ActiveSessionId = null;
        }
    }

    /// <summary>Sends a mode change command for the active session.</summary>
    [RelayCommand]
    private async Task ChangeModeAsync()
    {
        if (ActiveSessionId is not { } id) return;
        await mediator.Send(new ChangeModeCommand(id, Mode, AudioSource));
        IsMicActive         = AudioSource is AudioSourceMode.MicrophoneOnly or AudioSourceMode.Both;
        IsSystemAudioActive = AudioSource is AudioSourceMode.SystemAudioOnly or AudioSourceMode.Both;
    }

    partial void OnModeChanged(SessionMode value)
    {
        if (IsSessionActive) _ = ChangeModeAsync();
    }

    partial void OnAudioSourceChanged(AudioSourceMode value)
    {
        if (IsSessionActive) _ = ChangeModeAsync();
    }

    /// <summary>Toggles the visibility of the sidebar panel.</summary>
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;
}
