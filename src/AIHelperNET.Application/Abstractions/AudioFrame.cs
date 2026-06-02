using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>A single audio frame captured from a device.</summary>
/// <param name="Samples">PCM float samples.</param>
/// <param name="Speaker">Who produced this audio.</param>
/// <param name="CapturedAt">Timestamp of capture.</param>
public sealed record AudioFrame(float[] Samples, Speaker Speaker, DateTimeOffset CapturedAt);

/// <summary>Device identifiers for mic and loopback capture.</summary>
/// <param name="MicDeviceId">Microphone device ID, or null for default.</param>
/// <param name="LoopbackDeviceId">Loopback (speaker) device ID, or null for default.</param>
public sealed record AudioDeviceSelection(string? MicDeviceId, string? LoopbackDeviceId);

/// <summary>A transcribed speech segment.</summary>
/// <param name="Text">Transcribed text.</param>
/// <param name="Speaker">Who spoke.</param>
/// <param name="CapturedAt">When this segment was captured.</param>
/// <param name="Confidence">Transcription confidence in [0, 1].</param>
public sealed record TranscriptSegment(string Text, Speaker Speaker, DateTimeOffset CapturedAt, float Confidence);

/// <summary>Whisper model size to use for transcription.</summary>
public enum WhisperModelSize
{
    /// <summary>Tiny model — fastest, lowest accuracy.</summary>
    Tiny,
    /// <summary>Base model.</summary>
    Base,
    /// <summary>Small model.</summary>
    Small,
    /// <summary>Medium model — balanced speed and accuracy.</summary>
    Medium
}

/// <summary>Selects which AI backend to use for answer generation.</summary>
public enum AiBackend
{
    /// <summary>Anthropic Claude via API.</summary>
    Claude,
    /// <summary>Ollama local model.</summary>
    Ollama
}
