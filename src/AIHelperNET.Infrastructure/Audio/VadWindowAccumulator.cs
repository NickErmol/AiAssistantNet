using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Infrastructure.Audio;

/// <summary>
/// Pure hysteresis state machine for Silero VAD probabilities.
/// Caller feeds per-chunk probabilities; this class manages the speech/silence window lifecycle.
/// </summary>
public sealed class VadWindowAccumulator
{
    private const float SpeechStartThreshold    = 0.50f;
    private const float SpeechContinueThreshold = 0.35f;
    private const int   StartConfirmCount = 2;   // consecutive chunks ≥ 0.5 required to start
    private const int   SilenceFlushCount = 12;  // sub-threshold chunks before flush (~375 ms)
    public  const int   MinChunks = 8;           // minimum chunks to emit a window (~250 ms)
    public  const int   MaxChunks = 240;         // force-flush threshold (~7.5 s)

    private bool          _inSpeech;
    private int           _confirmCount;
    private int           _silenceCount;
    private int           _chunkCount;
    private readonly List<float> _confirmBuffer = new();
    private readonly List<float> _buffer        = new();
    private Speaker       _lastSpeaker;

    /// <summary>
    /// Feed one 512-sample chunk with its Silero speech probability.
    /// Returns a completed <see cref="SpeechWindow"/> when the window is ready; otherwise null.
    /// </summary>
    public SpeechWindow? Feed(float probability, float[] samples, Speaker speaker)
    {
        if (!_inSpeech)
        {
            if (probability >= SpeechStartThreshold)
            {
                _confirmCount++;
                _confirmBuffer.AddRange(samples);
                _lastSpeaker = speaker;

                if (_confirmCount >= StartConfirmCount)
                {
                    _inSpeech     = true;
                    _silenceCount = 0;
                    _chunkCount   = _confirmCount;
                    _buffer.AddRange(_confirmBuffer);
                    _confirmBuffer.Clear();
                    _confirmCount = 0;

                    if (_chunkCount >= MaxChunks) return FlushWindow();
                }
            }
            else
            {
                _confirmCount = 0;
                _confirmBuffer.Clear();
            }
            return null;
        }

        // In speech
        _buffer.AddRange(samples);
        _lastSpeaker = speaker;

        if (probability >= SpeechContinueThreshold)
        {
            _silenceCount = 0;
            _chunkCount++;
        }
        else
        {
            _silenceCount++;
        }

        if (_chunkCount >= MaxChunks)
            return FlushWindow();

        if (_silenceCount >= SilenceFlushCount)
        {
            if (_chunkCount >= MinChunks) return FlushWindow();
            Reset();
        }

        return null;
    }

    /// <summary>Force-flushes remaining buffer (call on stream end). Returns null if buffer is too short.</summary>
    public SpeechWindow? Flush()
    {
        if (!_inSpeech || _chunkCount < MinChunks) { Reset(); return null; }
        return FlushWindow();
    }

    private SpeechWindow FlushWindow()
    {
        var win = new SpeechWindow([.. _buffer], _lastSpeaker);
        Reset();
        return win;
    }

    private void Reset()
    {
        _inSpeech     = false;
        _confirmCount = 0;
        _silenceCount = 0;
        _chunkCount   = 0;
        _buffer.Clear();
        _confirmBuffer.Clear();
    }
}
