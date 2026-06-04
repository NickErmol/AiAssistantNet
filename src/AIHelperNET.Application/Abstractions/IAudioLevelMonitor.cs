namespace AIHelperNET.Application.Abstractions;

/// <summary>Monitors real-time audio peak levels for mic and loopback independently of session state.</summary>
public interface IAudioLevelMonitor
{
    /// <summary>Fired when the microphone peak level changes. Value in [0.0, 1.0].</summary>
    event Action<double> MicLevelChanged;

    /// <summary>Fired when the system audio (loopback) peak level changes. Value in [0.0, 1.0].</summary>
    event Action<double> SystemLevelChanged;

    /// <summary>Start level monitoring using the configured device IDs.</summary>
    Task StartAsync(string? micDeviceId, string? loopbackDeviceId, CancellationToken ct);

    /// <summary>Stop monitoring and release devices.</summary>
    Task StopAsync();
}
