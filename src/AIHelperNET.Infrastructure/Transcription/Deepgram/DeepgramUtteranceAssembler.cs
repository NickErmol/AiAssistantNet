namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Accumulates finalized chunks (is_final) into one utterance, emitted when speech_final closes it.
/// Deepgram splits long continuous speech into several finalized chunks before the endpoint fires.
/// </summary>
public sealed class DeepgramUtteranceAssembler
{
    private readonly List<string> _parts = [];
    private readonly List<float> _confidences = [];
    private double _startSeconds = -1;

    /// <summary>Feeds one chunk; returns the completed utterance when speech_final closes it, else null.</summary>
    public DeepgramUtterance? Add(DeepgramResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Transcript))
        {
            if (_startSeconds < 0) _startSeconds = result.StartSeconds;
            _parts.Add(result.Transcript.Trim());
            _confidences.Add(result.Confidence);
        }

        if (!result.SpeechFinal) return null;
        if (_parts.Count == 0)
        {
            Reset();
            return null;
        }

        var utterance = new DeepgramUtterance(
            string.Join(' ', _parts), _confidences.Average(), _startSeconds);
        Reset();
        return utterance;
    }

    private void Reset()
    {
        _parts.Clear();
        _confidences.Clear();
        _startSeconds = -1;
    }
}
