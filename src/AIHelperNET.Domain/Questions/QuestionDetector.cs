namespace AIHelperNET.Domain.Questions;

/// <summary>Detects whether a transcript segment contains an interview question and deduplicates against recent questions.</summary>
public sealed class QuestionDetector
{
    private const double DuplicateThreshold = 0.6;
    private const int MinWords = 4;

    private static readonly HashSet<string> Interrogatives = new(StringComparer.OrdinalIgnoreCase)
    {
        "what","why","how","when","where","which","who","can","could","would",
        "will","do","does","did","is","are","should"
    };

    private static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "explain","describe","write","implement","design","compare","optimize",
        "refactor","debug","walk","tell","give","show"
    };

    // Whisper hallucination phrases that start with interrogative words and would otherwise pass.
    // Compared after stripping punctuation and lowercasing.
    private static readonly HashSet<string> NoisePhrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "software engineering system design coding",
        "software engineering system design",
        "software engineering system design data structure coding",
        "software engineering system design software engineering",
        "software engineering system design algorithms data structures coding",
    };

    /// <summary>
    /// Evaluates <paramref name="text"/> and returns a <see cref="QuestionDetectionResult"/>
    /// indicating whether it is a question, a duplicate, or neither.
    /// </summary>
    /// <param name="text">The transcript segment to evaluate.</param>
    /// <param name="recentQuestions">Previously detected questions used for duplicate detection.</param>
#pragma warning disable CA1822 // instance method intentional — callers hold a QuestionDetector reference
    public QuestionDetectionResult Evaluate(string text, IReadOnlyCollection<string> recentQuestions)
#pragma warning restore CA1822
    {
        if (string.IsNullOrWhiteSpace(text))
            return QuestionDetectionResult.NotAQuestion();

        var normalized = text.Trim();

        if (IsNoisePhrase(normalized))
            return QuestionDetectionResult.NotAQuestion();

        if (!LooksLikeQuestion(normalized))
            return QuestionDetectionResult.NotAQuestion();

        var candidateTokens = Tokenize(normalized);
        foreach (var prior in recentQuestions)
        {
            if (Jaccard(candidateTokens, Tokenize(prior)) >= DuplicateThreshold)
                return QuestionDetectionResult.Duplicate();
        }
        return QuestionDetectionResult.NewQuestion(normalized);
    }

    private static bool LooksLikeQuestion(string text)
    {
        if (text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
            return false;
        if (text.EndsWith('?')) return true;
        var first = FirstWord(text);
        return Interrogatives.Contains(first) || ImperativeVerbs.Contains(first);
    }

    private static bool IsNoisePhrase(string text)
    {
        var stripped = new string(
            text.ToLowerInvariant()
                .Where(c => char.IsLetterOrDigit(c) || c == ' ')
                .ToArray())
            .Trim();
        // Also try collapsing repeated words (e.g. "software engineering software engineering")
        return NoisePhrases.Contains(stripped) ||
               NoisePhrases.Any(p => stripped.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Computes the Jaccard similarity coefficient between two token sets.</summary>
    /// <param name="a">First token set.</param>
    /// <param name="b">Second token set.</param>
    /// <returns>A value in [0.0, 1.0]; returns 1.0 when both sets are empty.</returns>
    public static double Jaccard(IReadOnlySet<string> a, IReadOnlySet<string> b)
    {
        if (a.Count == 0 && b.Count == 0) return 1.0;
        if (a.Count == 0 || b.Count == 0) return 0.0;
        var intersection = a.Count(b.Contains);
        var union = a.Count + b.Count - intersection;
        return union == 0 ? 0.0 : (double)intersection / union;
    }

    private static HashSet<string> Tokenize(string text) =>
        text.ToLowerInvariant()
            .Split([' ', ',', '.', '?', '!', ';', ':', '\n', '\t'],
                   StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet();

    private static string FirstWord(string text)
    {
        var idx = text.IndexOf(' ');
        return (idx < 0 ? text : text[..idx]).Trim('?', '.', ',', '!');
    }
}
