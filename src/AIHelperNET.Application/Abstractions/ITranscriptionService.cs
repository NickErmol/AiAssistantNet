namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for transcribing audio frames to text segments.</summary>
public interface ITranscriptionService
{
    /// <summary>Transcribes an audio stream to transcript segments.</summary>
    /// <param name="frames">Source audio frames.</param>
    /// <param name="model">Whisper model size to use.</param>
    /// <param name="ct">Cancellation token.</param>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, WhisperModelSize model, CancellationToken ct);
}
