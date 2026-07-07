using System.Runtime.CompilerServices;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Glossary;
using AIHelperNET.Infrastructure.Security;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Streams session audio to Deepgram's live WebSocket and yields one segment per endpointed
/// utterance (speech_final). Finals-only: interim results are disabled at the protocol level.
/// Faults (connect failure, socket drop, auth error) propagate to the caller —
/// ResilientTranscriptionService (next task) owns retry/fallback policy.
/// </summary>
public sealed class DeepgramTranscriptionService(
    IDeepgramSocketFactory socketFactory,
    ISecretStore secrets,
    ITranscriptionGlossaryProvider glossary,
    TimeSpan? keepAliveInterval = null) : ITranscriptionService
{
    private const int MinWords = 3;              // parity with WhisperTranscriptionService
    private const int KeytermWordBudget = 60;    // parity with the Whisper glossary budget
    private const string KeepAliveJson = """{"type":"KeepAlive"}""";
    private const string CloseStreamJson = """{"type":"CloseStream"}""";
    private readonly TimeSpan _keepAlive = keepAliveInterval ?? TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Wait for the first frame before opening the socket: gets the speaker, and an
        // AudioSourceMode-disabled stream never connects (never bills).
        await using var enumerator = frames.GetAsyncEnumerator(ct);
        if (!await enumerator.MoveNextAsync()) yield break;
        var speaker = enumerator.Current.Speaker;

        var keyResult = secrets.GetApiKey(SecretKind.Deepgram);
        if (keyResult.IsFailed)
            throw new InvalidOperationException("No Deepgram API key stored.");

        await using var socket = socketFactory.Create();
        var apiKey = SecureStringHelpers.ConvertToString(keyResult.Value);
        try
        {
            await socket.ConnectAsync(BuildUri(options), apiKey, ct);
        }
        finally
        {
            SecureStringHelpers.ZeroString(apiKey);
        }

        var epoch = DateTimeOffset.UtcNow;   // stream-relative t=0 for CapturedAt mapping
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendTask = SendLoopAsync(socket, enumerator, linked.Token);
        _ = sendTask.ContinueWith(
            _ => linked.Cancel(),   // a dead send loop must not leave the receive loop hanging
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        var assembler = new DeepgramUtteranceAssembler();
        while (true)
        {
            string? json;
            try
            {
                json = await socket.ReceiveTextAsync(linked.Token);
            }
            catch (OperationCanceledException) when (sendTask.IsFaulted)
            {
                // A send fault always wins classification — even if the caller cancelled in the
                // same instant. Otherwise the real WebSocket/auth exception is swallowed as a
                // bare cancellation and the resilient wrapper never sees a fault to retry.
                break;   // surface the send fault below
            }
            if (json is null) break;

            var result = DeepgramResultParser.Parse(json);
            if (result is null) continue;
            var utterance = assembler.Add(result);
            if (utterance is null) continue;
            if (utterance.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
                continue;

            Log.Information("DeepgramTiming speaker={Speaker} startSec={Start:F2} conf={Conf:F2}",
                speaker, utterance.StartSeconds, utterance.Confidence);
            yield return new TranscriptSegment(
                utterance.Text, speaker, epoch.AddSeconds(utterance.StartSeconds), utterance.Confidence);
        }

        await sendTask;   // propagate send-side faults as the stream's failure
    }

    private async Task SendLoopAsync(
        IDeepgramSocket socket, IAsyncEnumerator<AudioFrame> frames, CancellationToken ct)
    {
        // Caller has already advanced to the first frame.
        var hasCurrent = true;
        while (hasCurrent)
        {
            await socket.SendAudioAsync(PcmConverter.ToLinear16(frames.Current.Samples), ct);

            // Await the next frame, keeping the socket alive during capture gaps. The idle
            // delay gets its own token: frames normally arrive every ~30-100ms, and without
            // an explicit cancel each round would orphan a live 5s timer (timer-queue churn
            // that scales with session length).
            var next = frames.MoveNextAsync().AsTask();
            while (true)
            {
                using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var completed = await Task.WhenAny(next, Task.Delay(_keepAlive, idleCts.Token));
                if (completed == next)
                {
                    idleCts.Cancel();   // release the losing delay's timer immediately
                    hasCurrent = await next;
                    break;
                }
                await socket.SendTextAsync(KeepAliveJson, ct);
            }
        }
        await socket.SendTextAsync(CloseStreamJson, ct);   // server flushes remaining finals, then closes
    }

    private Uri BuildUri(TranscriptionOptions options)
    {
        // endpointing=300ms: long enough to ride out mid-sentence pauses, far below the
        // ~3.5s batch-Whisper latency this feature exists to beat.
        var sb = new StringBuilder("wss://api.deepgram.com/v1/listen")
            .Append("?model=nova-3&encoding=linear16&sample_rate=16000&channels=1")
            .Append("&smart_format=true&interim_results=false&endpointing=300");

        if (!string.IsNullOrWhiteSpace(options.Language)
            && !options.Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            sb.Append("&language=").Append(Uri.EscapeDataString(options.Language));

        if (options.GlossaryDomains.Count > 0)
        {
            var terms = GlossarySelector.Select(
                glossary.Domains, options.GlossaryDomains,
                recentContext: string.Empty, KeytermWordBudget);
            foreach (var term in terms)
                sb.Append("&keyterm=").Append(Uri.EscapeDataString(term));
        }

        return new Uri(sb.ToString());
    }
}
