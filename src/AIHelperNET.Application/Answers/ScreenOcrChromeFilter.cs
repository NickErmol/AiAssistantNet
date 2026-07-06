using System.Text.RegularExpressions;

namespace AIHelperNET.Application.Answers;

/// <summary>Detects OCR captures that contain only window/meeting UI chrome (title bars, participant
/// lists, call controls) rather than an actual on-screen task. Such captures must not create screen
/// turns: in a live session a captured Teams title bar became the "task in focus" and misrouted all
/// subsequent interviewer speech. Pure — no I/O, no AI call.</summary>
public static partial class ScreenOcrChromeFilter
{
    // Real task text almost always exceeds this; chrome captures (title bar + call controls) are short.
    private const int MaxChromeWords = 40;

    // Meeting-UI vocabulary (Teams/Zoom/Meet controls and roster labels). Matched as whole words,
    // letters only, case-insensitive. Individually these are ordinary words ("chat", "react"), so a
    // single hit never rejects — see the combined rules in IsLikelyChrome.
    private static readonly HashSet<string> MeetingWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "notetaker", "participants", "participant", "mute", "unmute", "reactions",
        "raise", "chat", "people", "invite", "breakout", "leave", "react", "camera",
    };

    // Any of these means the capture carries answerable content, never chrome.
    private static readonly string[] ContentMarkers =
        ["Question:", "Task:", "Problem:", "Prompt:", "Challenge:", "Exercise:"];

    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b")]
    private static partial Regex ClockPattern();

    /// <summary>Returns <see langword="true"/> when <paramref name="ocr"/> looks like meeting/window
    /// UI chrome with no answerable task content.</summary>
    public static bool IsLikelyChrome(string? ocr)
    {
        if (string.IsNullOrWhiteSpace(ocr))
            return true;

        // Strong content signals: questions, code, or an explicit task marker.
        if (ocr.Contains('?') || ocr.Contains('{') || ocr.Contains(';') || ocr.Contains("```"))
            return false;
        foreach (var marker in ContentMarkers)
            if (ocr.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return false;

        var tokens = ocr.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length >= MaxChromeWords)
            return false;

        var meetingHits = tokens
            .Select(LettersOnly)
            .Where(w => w.Length > 0 && MeetingWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var clockHits = ClockPattern().Count(ocr);
        var noiseRatio = tokens.Count(IsNoiseToken) / (double)tokens.Length;

        // A rejection always needs at least two independent chrome signals so ordinary sentences
        // that happen to contain "chat"/"react"/a clock time pass through.
        return meetingHits >= 3
            || (meetingHits >= 2 && (clockHits >= 1 || noiseRatio >= 0.2))
            || (meetingHits >= 1 && clockHits >= 1 && noiseRatio >= 0.15);
    }

    private static string LettersOnly(string token)
        => new(token.Where(char.IsLetter).ToArray());

    // Roster initials ("KS", "K", "A1"), lone digits ("0", ".11"), and OCR garble ("tl•dV") — the
    // token shrapnel a title bar + participant list produces.
    private static bool IsNoiseToken(string token)
    {
        if (token.Contains('•'))
            return true;
        var trimmed = token.Trim(',', '.', ':', '-');
        if (trimmed.Length == 0)
            return true;
        if (trimmed.All(char.IsDigit))
            return true;
        return trimmed.Length <= 2 && trimmed.All(c => char.IsUpper(c) || char.IsDigit(c));
    }
}
