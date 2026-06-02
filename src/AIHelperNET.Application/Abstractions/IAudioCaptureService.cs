namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for capturing audio from mic and loopback devices.</summary>
public interface IAudioCaptureService
{
    /// <summary>Streams audio frames from the specified devices.</summary>
    /// <param name="selection">Device identifiers to capture from.</param>
    /// <param name="ct">Cancellation token to stop capture.</param>
    IAsyncEnumerable<AudioFrame> CaptureAsync(AudioDeviceSelection selection, CancellationToken ct);
}
