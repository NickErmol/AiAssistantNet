using System.Text;

namespace AIHelperNET.Application.Answers;

/// <summary>Result of adding a capture to a <see cref="ScreenCaptureAccumulator"/>.</summary>
/// <param name="IsNewGroup">True if this capture started a new group (caller should create a new card).</param>
/// <param name="CombinedOcr">All captures in the current group, labeled and concatenated.</param>
/// <param name="Count">Number of captures in the current group.</param>
public sealed record ScreenCaptureAddResult(bool IsNewGroup, string CombinedOcr, int Count);

/// <summary>
/// Groups consecutive screen captures by recency: captures less than <c>gap</c> apart belong to
/// one task and are combined; a longer gap (or <see cref="Reset"/>) starts a new group. Pure — the
/// caller supplies the timestamp.
/// </summary>
public sealed class ScreenCaptureAccumulator(TimeSpan gap)
{
    private readonly List<string> _captures = [];
    private DateTimeOffset _lastCaptureAt;
    private bool _hasGroup;

    /// <summary>Adds a capture's OCR and returns the resulting group state.</summary>
    public ScreenCaptureAddResult Add(string ocr, DateTimeOffset now)
    {
        var isNewGroup = !_hasGroup || (now - _lastCaptureAt) >= gap;
        if (isNewGroup)
            _captures.Clear();

        _captures.Add(ocr);
        _lastCaptureAt = now;
        _hasGroup = true;

        return new ScreenCaptureAddResult(isNewGroup, Combine(_captures), _captures.Count);
    }

    /// <summary>Ends the current group; the next <see cref="Add"/> starts fresh.</summary>
    public void Reset()
    {
        _captures.Clear();
        _hasGroup = false;
    }

    private static string Combine(List<string> captures)
    {
        if (captures.Count == 1)
            return captures[0];

        var sb = new StringBuilder();
        for (var i = 0; i < captures.Count; i++)
        {
            if (i > 0) sb.Append("\n\n");
            sb.Append("--- Screen ").Append(i + 1).Append(" ---\n").Append(captures[i]);
        }
        return sb.ToString();
    }
}
