using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Infrastructure.Audio;
using Whisper.net;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(WhisperModelProvider models) : ITranscriptionService
{
    // Serialises Build() across mic and loopback tasks. Concurrent KV-cache allocation for
    // medium/large models causes both builds to stall indefinitely; sequential builds complete.
    private static readonly SemaphoreSlim _buildLock = new(1, 1);

    private const int MinWords = 3;

    private const string InitialPrompt =
        "Technical interview. Software engineering, system design, algorithms, data structures, coding.";

    private static readonly HashSet<string> HallucinationPhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you", "thanks for watching", "thanks for listening",
        "please subscribe", "like and subscribe", "see you next time",
        "subtitles by", "transcribed by",
    };

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        string language,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var factory = await models.GetFactoryAsync(model, ct);
        var lang = string.IsNullOrWhiteSpace(language) || language == "auto" ? null : language;

        // Processor is built lazily on the first speech window so the VAD loop starts immediately.
        // The build lock serialises mic and loopback builds: concurrent KV-cache allocation for
        // medium/large models stalls both builds indefinitely; sequential builds complete normally.
        string? lastEmitted = null;
        WhisperProcessor? processor = null;

        try
        {
        await foreach (var window in VoiceActivityDetector.AccumulateSpeechWindows(frames, ct))
        {
            if (processor is null)
            {
                await _buildLock.WaitAsync(ct);
                try
                {
                    processor = factory.CreateBuilder()
                        .WithLanguage(lang ?? "en")
                        .WithTemperature(0)            // greedy decoding — eliminates random word substitutions
                        .WithPrompt(InitialPrompt)     // primes vocabulary, reduces nonsense outputs
                        .WithNoSpeechThreshold(0.6f)   // drop segments Whisper itself flags as silent
                        .WithSingleSegment()           // one result per VAD window — prevents within-window splits
                        .Build();
                }
                finally { _buildLock.Release(); }
            }

            await foreach (var seg in processor!.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (IsKnownHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                lastEmitted = seg.Text.Trim();
                yield return new TranscriptSegment(
                    lastEmitted,
                    window.Speaker,
                    DateTimeOffset.UtcNow,
                    seg.Probability);
            }
        }
        } // end try
        finally { if (processor is not null) await ((IAsyncDisposable)processor).DisposeAsync(); }
    }

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsKnownHallucination(string text)
    {
        var trimmed = text.Trim('.', '!', '?', ' ');
        return HallucinationPhrases.Contains(trimmed);
    }

    private static bool IsNearDuplicate(string current, string? previous)
    {
        if (previous is null) return false;
        var a = Tokenize(current);
        var b = Tokenize(previous);
        return QuestionDetector.Jaccard(a, b) >= 0.85;
    }

    private static HashSet<string> Tokenize(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', '.', ',', '?', '!'], StringSplitOptions.RemoveEmptyEntries)];
}
