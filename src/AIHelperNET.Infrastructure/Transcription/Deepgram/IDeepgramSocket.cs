namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>Thin seam over the Deepgram WebSocket so transcription logic is testable without a network.</summary>
public interface IDeepgramSocket : IAsyncDisposable
{
    /// <summary>Opens the WebSocket with Deepgram token auth.</summary>
    Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct);

    /// <summary>Sends one binary PCM frame.</summary>
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct);

    /// <summary>Sends one JSON control message (KeepAlive, CloseStream).</summary>
    Task SendTextAsync(string json, CancellationToken ct);

    /// <summary>Next text message from the server, or null once the socket has closed.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);
}

/// <summary>Creates one socket per transcription stream.</summary>
public interface IDeepgramSocketFactory
{
    /// <summary>Returns a fresh, unconnected socket.</summary>
    IDeepgramSocket Create();
}
