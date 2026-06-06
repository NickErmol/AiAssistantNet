using AIHelperNET.Application.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

/// <summary>Monitors real-time audio peak levels for mic and loopback independently of session state.</summary>
public sealed class AudioLevelMonitor : IAudioLevelMonitor, IAsyncDisposable
{
    /// <inheritdoc/>
    public event Action<double>? MicLevelChanged;

    /// <inheritdoc/>
    public event Action<double>? SystemLevelChanged;

    private WasapiCapture? _mic;
    private WasapiLoopbackCapture? _loopback;
    private CancellationTokenSource? _cts;

    /// <inheritdoc/>
    public Task StartAsync(string? micDeviceId, string? loopbackDeviceId, CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        using var enumerator = new MMDeviceEnumerator();

        try
        {
            var micDevice = micDeviceId is not null
                ? enumerator.GetDevice(micDeviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

            _mic = new WasapiCapture(micDevice);
            _mic.DataAvailable += (_, e) =>
            {
                var peak = ComputePeak(e.Buffer, e.BytesRecorded, _mic.WaveFormat);
                MicLevelChanged?.Invoke(peak);
            };
            _mic.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioLevelMonitor: failed to start mic capture");
        }

        try
        {
            var loopbackDevice = loopbackDeviceId is not null
                ? enumerator.GetDevice(loopbackDeviceId)
                : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _loopback = new WasapiLoopbackCapture(loopbackDevice);
            _loopback.DataAvailable += (_, e) =>
            {
                var peak = ComputePeak(e.Buffer, e.BytesRecorded, _loopback.WaveFormat);
                SystemLevelChanged?.Invoke(peak);
            };
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "AudioLevelMonitor: failed to start loopback capture");
        }

        Log.Debug("AudioLevelMonitor: started");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task StopAsync()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
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

    /// <inheritdoc/>
    public async ValueTask DisposeAsync() => await StopAsync();
}
