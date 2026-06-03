namespace AIHelperNET.Domain.Sessions;

/// <summary>Defines which audio sources are captured in a session.</summary>
public enum AudioSourceMode
{
    /// <summary>Only microphone audio is captured.</summary>
    MicrophoneOnly,

    /// <summary>Only system audio is captured.</summary>
    SystemAudioOnly,

    /// <summary>Both microphone and system audio are captured.</summary>
    Both
}
