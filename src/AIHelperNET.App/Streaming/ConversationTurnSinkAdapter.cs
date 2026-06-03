using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.App.Streaming;

/// <summary>Dispatches new-turn notifications to the UI thread.</summary>
public sealed class ConversationTurnSinkAdapter : IConversationTurnSink
{
    private Action<ConversationTurnId, string>? _handler;

    /// <summary>Registers the UI-thread callback.</summary>
    public void SetHandler(Action<ConversationTurnId, string> handler) => _handler = handler;

    /// <inheritdoc/>
    public void OnTurnCreated(ConversationTurnId turnId, string question)
    {
        if (_handler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _handler(turnId, question));
    }
}
