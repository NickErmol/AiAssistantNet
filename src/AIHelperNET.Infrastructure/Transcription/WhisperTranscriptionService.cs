using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Infrastructure.Audio;
using Serilog;
using Whisper.net;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(
    WhisperModelProvider whisperModels,
    SileroModelProvider  sileroModels,
    ITranscriptionGlossaryProvider glossary) : ITranscriptionService
{
    // Serialises Build() across mic and loopback tasks. Concurrent KV-cache allocation for
    // medium/large models causes both builds to stall indefinitely; sequential builds complete.
    private static readonly SemaphoreSlim _buildLock = new(1, 1);

    private const int MinWords = 3;
    private const int RecentContextSegments = 4;
    private const int RecentContextWordCap = 30;
    private const int GlossaryWordBudget = 60;

    private const string InitialPrompt =
        "Technical interview. Software engineering, system design, algorithms, data structures, coding.";

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        string language,
        IReadOnlySet<string> glossaryDomains,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var factory = await whisperModels.GetFactoryAsync(model, ct);
        // "auto" makes Whisper detect the language per window; a concrete code (e.g. "en", "ru")
        // forces it. Empty/whitespace falls back to auto rather than silently forcing English.
        var lang    = string.IsNullOrWhiteSpace(language) ? "auto" : language;

        string? lastEmitted = null;
        var recent = new Queue<string>(RecentContextSegments);

        string BuildPrompt()
        {
            var recentContext = LastWords(string.Join(' ', recent), RecentContextWordCap);
            var suffix = glossaryDomains.Count == 0
                ? string.Empty
                : glossary.BuildPromptSuffix(glossaryDomains, recentContext, GlossaryWordBudget);
            var basePart = recentContext.Length == 0 ? InitialPrompt : recentContext;
            // Glossary goes LAST so it sits closest to the audio and survives Whisper's
            // tail-truncation of an over-long prompt.
            return suffix.Length == 0 ? basePart : $"{basePart} {suffix}";
        }

        await foreach (var window in SileroVadDetector.AccumulateSpeechWindows(frames, sileroModels, ct))
        {
            await _buildLock.WaitAsync(ct);
            WhisperProcessor processor;
            var buildSw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                processor = factory.CreateBuilder()
                    .WithLanguage(lang)
                    .WithTemperature(0)            // greedy decoding — no random word substitutions
                    .WithNoContext()               // prevent stale KV-cache from previous windows
                    .WithPrompt(BuildPrompt())     // rolling context + glossary bias for every window
                    .WithNoSpeechThreshold(0.6f)
                    .WithSingleSegment()
                    .Build();
            }
            finally { _buildLock.Release(); }
            buildSw.Stop();

            await using var _ = (IAsyncDisposable)processor;

            var produced = new List<SegmentData>();
            var inferSw = System.Diagnostics.Stopwatch.StartNew();
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
                produced.Add(seg);
            inferSw.Stop();

            var audioSec = TranscriptionMetrics.WindowAudioSeconds(window.Samples.Length);
            Log.Information(
                "WhisperTiming model={Model} speaker={Speaker} windowAudioSec={AudioSec:F2} " +
                "buildMs={BuildMs} inferMs={InferMs} rtf={Rtf:F2}",
                model, window.Speaker, audioSec,
                buildSw.ElapsedMilliseconds, inferSw.ElapsedMilliseconds,
                TranscriptionMetrics.RealtimeFactor(inferSw.ElapsedMilliseconds, audioSec));

            foreach (var seg in produced)
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
                if (WordCount(seg.Text) < MinWords) continue;
                if (TranscriptHallucinationFilter.IsHallucination(seg.Text)) continue;
                if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

                var text = seg.Text.Trim();
                lastEmitted = text;
                recent.Enqueue(text);
                while (recent.Count > RecentContextSegments) recent.Dequeue();
                yield return new TranscriptSegment(text, window.Speaker, DateTimeOffset.UtcNow, seg.Probability);
            }
        }
    }

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string LastWords(string text, int maxWords)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length <= maxWords ? text : string.Join(' ', words[^maxWords..]);
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
