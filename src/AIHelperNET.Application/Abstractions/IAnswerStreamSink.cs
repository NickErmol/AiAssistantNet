using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for pushing streamed answer chunks to the UI layer.</summary>
public interface IAnswerStreamSink
{
    /// <summary>Pushes a streamed text chunk for the given turn and version type.</summary>
    /// <param name="turnId">The conversation turn being streamed.</param>
    /// <param name="versionType">The answer version type.</param>
    /// <param name="chunk">The text chunk.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct);

    /// <summary>Signals that streaming for the given turn and version type is complete.</summary>
    /// <param name="turnId">The conversation turn that finished streaming.</param>
    /// <param name="versionType">The answer version type.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct);

    /// <summary>Signals that an error occurred during streaming for the given turn.</summary>
    /// <param name="turnId">The conversation turn where the error occurred.</param>
    /// <param name="errorMessage">The error message.</param>
    /// <param name="ct">Cancellation token.</param>
    ValueTask OnErrorAsync(ConversationTurnId turnId, string errorMessage, CancellationToken ct);
}
