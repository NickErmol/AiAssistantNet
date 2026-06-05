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

        var uow = _sessionScope!.ServiceProvider.GetRequiredService<IUnitOfWork>();

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

        // Mic transcription task (Speaker.Me — candidate follow-ups).
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

        // Sequential consumer: pipeline.ProcessAsync must not be called concurrently.
        // Segments from a single Whisper window arrive back-to-back within milliseconds; segments
        // from different windows are separated by at least the VAD silence gap (~600 ms). Waiting
        // SegmentMergeWindowMs after the first segment lets us greedily merge consecutive
        // same-speaker fragments into one transcript item before question detection fires.
        const int SegmentMergeWindowMs = 300;

        var consumerTask = Task.Run(async () =>
        {
            TranscriptSegment? pending = null;

            async Task FlushAsync()
            {
                if (pending is null) return;
                Log.Information("SessionRunner: segment [{Speaker}] conf={Conf:F2} — {Text}",
                    pending.Speaker, pending.Confidence, pending.Text);
                var item = TranscriptItem.Create(pending.Speaker, pending.Text, pending.CapturedAt, pending.Confidence);
                await pipeline.ProcessAsync(session, item, uow, CancellationToken.None);
                pending = null;
            }

            try
            {
                while (true)
                {
                    TranscriptSegment next;

                    if (pending is null)
                    {
                        // No buffered segment — block until one arrives or channel closes.
                        if (!await mergeChannel.Reader.WaitToReadAsync(CancellationToken.None)) break;
                        if (!mergeChannel.Reader.TryRead(out next!)) continue;
                    }
                    else
                    {
                        // Have a buffered segment — wait briefly for a follow-on from the same speaker.
                        using var window = CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None);
                        window.CancelAfter(SegmentMergeWindowMs);
                        try
                        {
                            next = await mergeChannel.Reader.ReadAsync(window.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            await FlushAsync(); // timeout — no follow-on arrived
                            continue;
                        }
                    }

                    if (pending is null)
                    {
                        pending = next;
                    }
                    else if (next.Speaker == pending.Speaker)
                    {
                        // Same speaker: merge into one utterance.
                        pending = pending with { Text = pending.Text + " " + next.Text };
                    }
                    else
                    {
                        // Speaker changed: flush current, start new.
                        await FlushAsync();
                        pending = next;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error(ex, "SessionRunner: unhandled error in pipeline");
            }
            finally
            {
                await FlushAsync(); // drain whatever remains
            }
        }, CancellationToken.None);

        // Explicit sequencing: wait for transcription to finish, close merge channel, drain consumer.
        await Task.WhenAll(micTask, loopbackTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        mergeChannel.Writer.TryComplete();
        await consumerTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);

        await captureTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Log.Information("SessionRunner: pipeline stopped");
    }
}
