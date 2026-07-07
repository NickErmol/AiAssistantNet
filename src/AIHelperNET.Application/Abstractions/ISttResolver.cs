namespace AIHelperNET.Application.Abstractions;

/// <summary>Resolves the transcription service for the selected STT provider at session start.</summary>
public interface ISttResolver
{
    /// <summary>Returns the service for <paramref name="provider"/>, falling back to Whisper when Deepgram is unusable.</summary>
    ITranscriptionService Resolve(SttProvider provider);
}
