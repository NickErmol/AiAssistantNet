using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Infrastructure.Audio;

namespace AIHelperNET.Infrastructure.Transcription;

public sealed class WhisperTranscriptionService(WhisperModelProvider models) : ITranscriptionService
{
    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        WhisperModelSize model,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var factory = await models.GetFactoryAsync(model, ct);
        await using var processor = factory.CreateBuilder()
            .WithLanguage("en")
            .Build();

        string? lastEmitted = null;

        await foreach (var window in VoiceActivityDetector.AccumulateSpeechWindows(frames, ct))
        {
            await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
            {
                if (string.IsNullOrWhiteSpace(seg.Text)) continue;
                if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
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
