using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.App.Streaming;

/// <summary>Dispatches streamed answer events to the UI thread.</summary>
public sealed class AnswerStreamSink : IAnswerStreamSink
{
    private Action<ConversationTurnId, AnswerVersionType, string>? _chunkHandler;
    private Action<ConversationTurnId, AnswerVersionType>? _completeHandler;
    private Action<ConversationTurnId, string>? _errorHandler;

    /// <summary>Registers the UI-thread callbacks for chunk, complete, and error events.</summary>
    public void SetHandlers(
        Action<ConversationTurnId, AnswerVersionType, string> onChunk,
        Action<ConversationTurnId, AnswerVersionType> onComplete,
        Action<ConversationTurnId, string> onError)
    {
        _chunkHandler    = onChunk;
        _completeHandler = onComplete;
        _errorHandler    = onError;
    }

    /// <inheritdoc/>
    public ValueTask OnChunkAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        string chunk, CancellationToken ct)
    {
        if (_chunkHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _chunkHandler(turnId, versionType, chunk));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnCompleteAsync(ConversationTurnId turnId, AnswerVersionType versionType,
        CancellationToken ct)
    {
        if (_completeHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _completeHandler(turnId, versionType));
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask OnErrorAsync(ConversationTurnId turnId, string errorMessage,
        CancellationToken ct)
    {
        if (_errorHandler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _errorHandler(turnId, errorMessage));
        return ValueTask.CompletedTask;
    }
}
