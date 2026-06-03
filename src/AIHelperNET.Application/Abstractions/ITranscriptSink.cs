using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for pushing live transcript items to the UI layer.</summary>
public interface ITranscriptSink
{
    /// <summary>Called when a new transcript item is available.</summary>
    /// <param name="item">The transcript item.</param>
    void OnTranscriptItem(TranscriptItem item);
}
