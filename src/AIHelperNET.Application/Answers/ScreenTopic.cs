using System.Text.RegularExpressions;

namespace AIHelperNET.Application.Answers;

/// <summary>Derives a short, human-readable label for a captured screen task from its OCR text.</summary>
public static partial class ScreenTopic
{
    private const int MaxLength = 120;

    // The capture OCRs the whole foreground window, so the first line often leaks viewer chrome
    // (toolbar words, the image filename) before the real task text. A task marker, when present,
    // is the cleanest anchor; otherwise we strip a leading image-filename token.
    private static readonly string[] TaskMarkers =
        ["Question:", "Task:", "Problem:", "Prompt:", "Challenge:", "Exercise:"];

    [GeneratedRegex(@"^\s*(\S+\.(?:png|jpe?g|gif|bmp|webp|tiff?|txt|pdf|docx?|md)\b[\s:.-]*)+",
        RegexOptions.IgnoreCase)]
    private static partial Regex LeadingFileTokens();

    /// <summary>Returns the first non-empty OCR line — with leading viewer chrome stripped and
    /// trimmed to <see cref="MaxLength"/> chars — or <c>"Screen task"</c> when there is no usable text.</summary>
    /// <param name="ocr">The captured OCR text.</param>
    public static string Derive(string ocr)
    {
        if (!string.IsNullOrWhiteSpace(ocr))
        {
            foreach (var raw in ocr.Split('\n'))
            {
                var line = CleanChrome(raw.Trim());
                if (line.Length == 0) continue;
                return line.Length > MaxLength ? line[..MaxLength] : line;
            }
        }
        return "Screen task";
    }

    private static string CleanChrome(string line)
    {
        if (line.Length == 0) return line;

        // Prefer a task marker — everything before it is window chrome.
        foreach (var marker in TaskMarkers)
        {
            var idx = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) return line[idx..].Trim();
        }

        // Otherwise drop a leading run of image/file-name tokens (title-bar leakage).
        return LeadingFileTokens().Replace(line, "").Trim();
    }
}
