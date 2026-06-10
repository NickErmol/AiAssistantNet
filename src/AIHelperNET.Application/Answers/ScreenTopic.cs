namespace AIHelperNET.Application.Answers;

/// <summary>Derives a short, human-readable label for a captured screen task from its OCR text.</summary>
public static class ScreenTopic
{
    private const int MaxLength = 120;

    /// <summary>Returns the first non-empty OCR line trimmed to <see cref="MaxLength"/> chars,
    /// or <c>"Screen task"</c> when the OCR has no usable text.</summary>
    /// <param name="ocr">The captured OCR text.</param>
    public static string Derive(string ocr)
    {
        if (!string.IsNullOrWhiteSpace(ocr))
        {
            foreach (var raw in ocr.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                return line.Length > MaxLength ? line[..MaxLength] : line;
            }
        }
        return "Screen task";
    }
}
