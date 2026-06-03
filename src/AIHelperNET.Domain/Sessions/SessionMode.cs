namespace AIHelperNET.Domain.Sessions;

/// <summary>Defines the input modalities active in a session.</summary>
public enum SessionMode
{
    /// <summary>Only audio input is active.</summary>
    AudioOnly,

    /// <summary>Only screen capture is active.</summary>
    ScreenOnly,

    /// <summary>Both audio input and screen capture are active.</summary>
    AudioAndScreen
}
