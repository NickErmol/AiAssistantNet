using System.Runtime.CompilerServices;
using System.Threading.Channels;
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

        bool runMic      = audioSource != AudioSourceMode.SystemAudioOnly;
        bool runLoopback = audioSource != AudioSourceMode.MicrophoneOnly;

        // Two per-speaker channels feed two independent VAD+Whisper instances.
        var micChannel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var loopbackChannel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // Merge channel serialises calls to pipeline.ProcessAsync — Session is not thread-safe.
        var mergeChannel = Channel.CreateUnbounded<TranscriptSegment>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Capture task: fan out NAudio frames to the appropriate per-speaker channel.
        var captureTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in audioCapture.CaptureAsync(devices, ct))
                {
                    if (frame.Speaker == Speaker.Me && runMic)
                        await micChannel.Writer.WriteAsync(frame, ct);
                    else if (frame.Speaker == Speaker.Other && runLoopback)
                        await loopbackChannel.Writer.WriteAsync(frame, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                micChannel.Writer.TryComplete();
                loopbackChannel.Writer.TryComplete();
                Log.Information("SessionRunner: capture stopped");
            }
        }, ct);

        // Mic transcription task (Speaker.Me — interviewer follow-ups).
        var micTask = runMic
            ? Task.Run(async () =>
            {
                try
                {
                    await foreach (var seg in transcription
                        .TranscribeAsync(micChannel.Reader.ReadAllAsync(ct), model, ct)
                        .WithCancellation(ct))
                    {
                        await mergeChannel.Writer.WriteAsync(seg, ct);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct)
            : Task.CompletedTask;

        // Loopback transcription task (Speaker.Other — interviewer questions).
        var loopbackTask = runLoopback
            ? Task.Run(async () =>
            {
                try
                {
                    await foreach (var seg in transcription
                        .TranscribeAsync(loopbackChannel.Reader.ReadAllAsync(ct), model, ct)
                        .WithCancellation(ct))
                    {
                        await mergeChannel.Writer.WriteAsync(seg, ct);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct)
            : Task.CompletedTask;

        // Complete the merge channel once both transcription tasks finish.
        _ = Task.WhenAll(micTask, loopbackTask)
            .ContinueWith(_ => mergeChannel.Writer.TryComplete(), TaskScheduler.Default);

        // Sequential consumer: pipeline.ProcessAsync must not be called concurrently.
        try
        {
            await foreach (var seg in mergeChannel.Reader.ReadAllAsync(ct))
            {
                Log.Information("SessionRunner: segment [{Speaker}] conf={Conf:F2} — {Text}",
                    seg.Speaker, seg.Confidence, seg.Text);
                var item = TranscriptItem.Create(seg.Speaker, seg.Text, seg.CapturedAt, seg.Confidence);
                await pipeline.ProcessAsync(session, item, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionRunner: unhandled error in pipeline");
        }

        await captureTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await Task.WhenAll(micTask, loopbackTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Log.Information("SessionRunner: pipeline stopped");
    }
}
