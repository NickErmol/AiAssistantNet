namespace AIHelperNET.Application.Sessions;

/// <summary>
/// Buffers consecutive speech segments and flushes them when a gap larger than
/// the threshold is detected between successive segments.
/// </summary>
public sealed class SegmentAccumulator
{
    private const int GapThresholdSeconds = 3;

    private readonly List<string> _buffer = [];
    private DateTimeOffset _lastTimestamp;

    /// <summary>
    /// Adds a segment. Returns the combined buffered text if the gap since the last
    /// segment exceeds GapThresholdSeconds, otherwise returns null (segment buffered).
    /// </summary>
    public string? Add(string text, DateTimeOffset timestamp)
    {
        if (_buffer.Count > 0 &&
            (timestamp - _lastTimestamp).TotalSeconds > GapThresholdSeconds)
        {
            var flushed = string.Join(" ", _buffer);
            _buffer.Clear();
            _buffer.Add(text);
            _lastTimestamp = timestamp;
            return flushed;
        }

        _buffer.Add(text);
        _lastTimestamp = timestamp;
        return null;
    }

    /// <summary>Force-flushes the current buffer. Returns null if empty.</summary>
    public string? Flush()
    {
        if (_buffer.Count == 0) return null;
        var result = string.Join(" ", _buffer);
        _buffer.Clear();
        return result;
    }
}
