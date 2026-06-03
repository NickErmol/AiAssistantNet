using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AIHelperNET.App.Services;

/// <summary>Drives the audio → transcription → pipeline loop for an active session.</summary>
public sealed class SessionRunner(
    IServiceScopeFactory scopeFactory,
    IAudioCaptureService audioCapture,
    ITranscriptionService transcription,
    TranscriptPipelineService pipeline)
{
    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;
    private IServiceScope? _sessionScope;

    /// <summary>Loads the session and starts the audio pipeline in the background.</summary>
    public async Task StartAsync(
        SessionId sessionId,
        AudioDeviceSelection devices,
        WhisperModelSize model,
        AudioSourceMode audioSource)
    {
        _sessionScope = scopeFactory.CreateScope();
        var repo = _sessionScope.ServiceProvider.GetRequiredService<ISessionRepository>();

        var result = await repo.GetAsync(sessionId, CancellationToken.None);
        if (result.IsFailed)
        {
            Log.Warning("SessionRunner: failed to load session {Id} — {Errors}", sessionId, result.Errors);
            _sessionScope.Dispose();
            _sessionScope = null;
            return;
        }

        _cts          = new CancellationTokenSource();
        _pipelineTask = RunAsync(result.Value, devices, model, audioSource, _cts.Token);
    }

    /// <summary>Stops audio capture and waits for the pipeline to drain.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        if (_pipelineTask is not null)
            await _pipelineTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _sessionScope?.Dispose();
        _sessionScope = null;
        _cts          = null;
        _pipelineTask = null;
    }

    private async Task RunAsync(
        Session session,
        AudioDeviceSelection devices,
        WhisperModelSize model,
        AudioSourceMode audioSource,
        CancellationToken ct)
    {
        Log.Information("SessionRunner: pipeline starting (mode={AudioSource})", audioSource);
        try
        {
            var frames = FilterFrames(audioCapture.CaptureAsync(devices, ct), audioSource, ct);
            await foreach (var seg in transcription.TranscribeAsync(frames, model, ct).WithCancellation(ct))
            {
                var item = TranscriptItem.Create(seg.Speaker, seg.Text, seg.CapturedAt, seg.Confidence);
                await pipeline.ProcessAsync(session, item, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionRunner: unhandled error in pipeline");
        }
        Log.Information("SessionRunner: pipeline stopped");
    }

    private static async IAsyncEnumerable<AudioFrame> FilterFrames(
        IAsyncEnumerable<AudioFrame> source,
        AudioSourceMode mode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var frame in source.WithCancellation(ct))
        {
            if (mode == AudioSourceMode.MicrophoneOnly && frame.Speaker != Speaker.Me) continue;
            if (mode == AudioSourceMode.SystemAudioOnly && frame.Speaker != Speaker.Other) continue;
            yield return frame;
        }
    }
}
