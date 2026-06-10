using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Test <see cref="IConversationTurnSink"/> that records created turns (id + question) in order, so a
/// driver can learn the id of a card the pipeline/handler creates asynchronously without polling the DB.
/// </summary>
public sealed class CapturingTurnSink : IConversationTurnSink
{
    private readonly ConcurrentQueue<(ConversationTurnId Id, string Question)> _created = new();

    /// <summary>Turns announced via <see cref="OnTurnCreated"/>, in creation order.</summary>
    public IReadOnlyList<(ConversationTurnId Id, string Question)> Created => _created.ToArray();

    /// <inheritdoc/>
    public void OnTurnCreated(ConversationTurnId turnId, string question)
        => _created.Enqueue((turnId, question));

    /// <inheritdoc/>
    public void OnTurnStatusChanged(ConversationTurnId turnId, ConversationTurnStatus newStatus) { }
}
