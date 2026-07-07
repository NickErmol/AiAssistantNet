using System.Net.WebSockets;
using System.Text;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>ClientWebSocket-backed implementation; one instance per transcription stream.</summary>
public sealed class DeepgramClientWebSocket : IDeepgramSocket
{
    private readonly ClientWebSocket _socket = new();

    /// <inheritdoc />
    public Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Token {apiKey}");
        return _socket.ConnectAsync(uri, ct);
    }

    /// <inheritdoc />
    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
        => _socket.SendAsync(pcm, WebSocketMessageType.Binary, endOfMessage: true, ct).AsTask();

    /// <inheritdoc />
    public Task SendTextAsync(string json, CancellationToken ct)
        => _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text,
            endOfMessage: true, ct);

    /// <inheritdoc />
    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return builder.ToString();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
            }
            catch (Exception) { /* best-effort close; disposal must not throw */ }
        }
        _socket.Dispose();
    }
}

/// <summary>Default factory: a fresh real socket per stream.</summary>
public sealed class DeepgramClientWebSocketFactory : IDeepgramSocketFactory
{
    /// <inheritdoc />
    public IDeepgramSocket Create() => new DeepgramClientWebSocket();
}
