using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.App.Streaming;

/// <summary>Dispatches live transcript items to the UI thread.</summary>
public sealed class TranscriptSink : ITranscriptSink
{
    private Action<TranscriptItem>? _handler;

    /// <summary>Registers the UI-thread callback for incoming transcript items.</summary>
    public void SetHandler(Action<TranscriptItem> handler) => _handler = handler;

    /// <inheritdoc/>
    public void OnTranscriptItem(TranscriptItem item)
    {
        if (_handler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() => _handler(item));
    }
}
