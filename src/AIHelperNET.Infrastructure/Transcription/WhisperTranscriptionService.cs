using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Infrastructure.Audio;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(WhisperModelProvider models) : ITranscriptionService
{
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
        await using var processor = factory.CreateBuilder()
            .WithLanguage(lang ?? "en")
            .WithTemperature(0)            // greedy decoding — eliminates random word substitutions
            .WithPrompt(InitialPrompt)     // primes vocabulary, reduces nonsense outputs
            .WithNoSpeechThreshold(0.6f)   // drop segments Whisper itself flags as silent
            .WithSingleSegment()           // one result per VAD window — prevents within-window splits
            .Build();

        string? lastEmitted = null;

        await foreach (var window in VoiceActivityDetector.AccumulateSpeechWindows(frames, ct))
        {
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (IsKnownHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                lastEmitted = seg.Text;
                yield return new TranscriptSegment(
                    seg.Text.Trim(),
                    window.Speaker,
                    DateTimeOffset.UtcNow,
                    seg.Probability);
            }
        }
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
