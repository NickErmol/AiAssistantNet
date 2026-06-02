using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

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

        using var mic = new WasapiCapture(micDevice);
        using var loopback = new WasapiLoopbackCapture(loopbackDevice);

        mic.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, mic.WaveFormat),
                    Speaker.Me,
                    DateTimeOffset.UtcNow));

        loopback.DataAvailable += (_, e) =>
            channel.Writer.TryWrite(
                new AudioFrame(
                    Resampler.To16kMonoFloat(e.Buffer, e.BytesRecorded, loopback.WaveFormat),
                    Speaker.Other,
                    DateTimeOffset.UtcNow));

        mic.StartRecording();
        loopback.StartRecording();

        using var reg = ct.Register(() =>
        {
            mic.StopRecording();
            loopback.StopRecording();
            channel.Writer.TryComplete();
        });

        await foreach (var frame in channel.Reader.ReadAllAsync(ct))
            yield return frame;
    }
}
