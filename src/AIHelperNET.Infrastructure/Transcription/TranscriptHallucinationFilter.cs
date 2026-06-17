namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>
/// Detects Whisper's well-known filler hallucinations (YouTube-style sign-offs, non-speech
/// annotations) so they can be dropped from the transcript. Whisper emits these on silent or
/// very short audio windows.
/// </summary>
public static class TranscriptHallucinationFilter
{
    private static readonly HashSet<string> Phrases = new(StringComparer.OrdinalIgnoreCase)
    {
        "thank you", "thanks for watching", "thanks for listening",
        "please subscribe", "like and subscribe", "see you next time",
        "i'll see you next time", "bye", "goodbye",
        "subtitles by", "transcribed by",
    };

    /// <summary>
    /// True when the whole segment is a known hallucination: a non-speech annotation
    /// (e.g. "(audio cuts out)", "[Music]") or text whose every sentence is a filler phrase.
    /// Leading dashes/bullets that Whisper sometimes prepends are stripped before matching, and
    /// repeats like "- Bye. - Bye." are caught because every sentence must be a filler.
    /// </summary>
    public static bool IsHallucination(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim();

        // Whole-segment non-speech annotation: "(audio cuts out)", "[Music]", "- (silence)".
        var forBracket = trimmed.TrimStart('-', '–', '—', '•', ' ', '\t');
        if (forBracket.Length >= 2 &&
            ((forBracket[0] == '(' && forBracket[^1] == ')') ||
             (forBracket[0] == '[' && forBracket[^1] == ']')))
            return true;

        // Every sentence must be a known filler phrase for the segment to count as hallucination.
        var sentences = trimmed.Split(['.', '!', '?'], StringSplitOptions.RemoveEmptyEntries);
        if (sentences.Length == 0) return false;
        foreach (var sentence in sentences)
        {
            var norm = NormalizeToLetters(sentence);
            if (norm.Length == 0) continue;            // pure punctuation/dash between fillers
            if (!Phrases.Contains(norm)) return false; // any real sentence => keep the segment
        }
        return true;
    }

    /// <summary>Trims leading/trailing non-letter characters (dashes, bullets, punctuation) and lowercases.</summary>
    private static string NormalizeToLetters(string s)
    {
        s = s.Trim();
        int start = 0, end = s.Length;
        while (start < end && !char.IsLetter(s[start])) start++;
        while (end > start && !char.IsLetter(s[end - 1])) end--;
        return s[start..end].ToLowerInvariant();
    }
}
