namespace AIHelperNET.Domain.Sessions;

/// <summary>
/// Represents the current state of a session.
/// </summary>
public enum SessionState
{
    /// <summary>The session is currently active.</summary>
    Active,

    /// <summary>The session has been stopped.</summary>
    Stopped
}
