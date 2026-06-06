using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.App.Streaming;

/// <summary>Dispatches new-turn notifications to the UI thread.</summary>
public sealed class ConversationTurnSinkAdapter : IConversationTurnSink
{
    private Action<ConversationTurnId, string>? _handler;
    private Action<ConversationTurnId, ConversationTurnStatus>? _statusHandler;

    /// <summary>Registers the UI-thread callback.</summary>
    public void SetHandler(Action<ConversationTurnId, string> handler) => _handler = handler;

    /// <summary>Registers the UI-thread status-change callback.</summary>
    public void SetStatusHandler(Action<ConversationTurnId, ConversationTurnStatus> handler)
        => _statusHandler = handler;

    /// <inheritdoc/>
    public void OnTurnCreated(ConversationTurnId turnId, string question)
    {
        if (_handler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _handler(turnId, question));
    }

    /// <inheritdoc/>
    public void OnTurnStatusChanged(ConversationTurnId turnId, ConversationTurnStatus newStatus)
    {
        if (_statusHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _statusHandler(turnId, newStatus));
    }
}
