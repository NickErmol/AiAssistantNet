using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using Serilog;

namespace AIHelperNET.Infrastructure.Audio;

public sealed class NAudioCaptureService : IAudioCaptureService
{
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        using var enumerator = new MMDeviceEnumerator();

        var micDevice = selection.MicDeviceId is not null
            ? enumerator.GetDevice(selection.MicDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);

        var loopbackDevice = selection.LoopbackDeviceId is not null
            ? enumerator.GetDevice(selection.LoopbackDeviceId)
            : enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        Log.Information("NAudio: mic={Mic}, loopback={Loopback}", micDevice.FriendlyName, loopbackDevice.FriendlyName);

        using var mic = new WasapiCapture(micDevice);
        using var loopback = new WasapiLoopbackCapture(loopbackDevice);

        int micFrames = 0, loopbackFrames = 0;

        mic.DataAvailable += (_, e) =>
        {
            if (++micFrames % 50 == 1)
                Log.Debug("NAudio: mic frame #{N} bytes={B}", micFrames, e.BytesRecorded);
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, mic.WaveFormat),
                    Speaker.Me,
                    DateTimeOffset.UtcNow));
        };

        loopback.DataAvailable += (_, e) =>
        {
            if (++loopbackFrames % 50 == 1)
                Log.Debug("NAudio: loopback frame #{N} bytes={B}", loopbackFrames, e.BytesRecorded);
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, loopback.WaveFormat),
                    Speaker.Other,
                    DateTimeOffset.UtcNow));
        };

        mic.StartRecording();
        loopback.StartRecording();
        Log.Information("NAudio: capture started");

        using var reg = ct.Register(() =>
        {
            mic.StopRecording();
            loopback.StopRecording();
            channel.Writer.TryComplete();
            Log.Information("NAudio: capture stopped (mic={M} loopback={L} frames total)", micFrames, loopbackFrames);
        });

        await foreach (var frame in channel.Reader.ReadAllAsync(ct))
            yield return frame;
    }
}
