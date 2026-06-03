using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for notifying the UI when a new conversation turn is created.</summary>
public interface IConversationTurnSink
{
    /// <summary>Called when a new turn is opened from a detected question.</summary>
    void OnTurnCreated(ConversationTurnId turnId, string question);
}
