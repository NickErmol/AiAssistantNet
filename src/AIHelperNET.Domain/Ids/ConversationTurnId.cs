namespace AIHelperNET.Domain.Ids;

/// <summary>Strongly-typed identifier for a conversation turn.</summary>
public readonly record struct ConversationTurnId(Guid Value)
{
    /// <summary>Creates a new unique conversation turn identifier.</summary>
    public static ConversationTurnId New() => new(Guid.CreateVersion7());
}
