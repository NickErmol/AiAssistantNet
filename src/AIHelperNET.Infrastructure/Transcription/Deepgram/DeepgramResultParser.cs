using System.Text.Json;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>One finalized transcription chunk from a Deepgram Results message.</summary>
/// <param name="Transcript">Chunk transcript text (may be empty).</param>
/// <param name="Confidence">Deepgram confidence in [0, 1].</param>
/// <param name="SpeechFinal">True when endpointing closed the utterance with this chunk.</param>
/// <param name="StartSeconds">Chunk start, seconds from stream start.</param>
public sealed record DeepgramResult(string Transcript, float Confidence, bool SpeechFinal, double StartSeconds);

/// <summary>A complete utterance assembled from one or more chunks (closed by speech_final).</summary>
/// <param name="Text">Joined utterance text.</param>
/// <param name="Confidence">Average chunk confidence in [0, 1].</param>
/// <param name="StartSeconds">First chunk's start, seconds from stream start.</param>
public sealed record DeepgramUtterance(string Text, float Confidence, double StartSeconds);

/// <summary>Parses Deepgram live-streaming messages; anything but a well-formed Results message is null.</summary>
public static class DeepgramResultParser
{
    /// <summary>Parses one WebSocket text message; returns null for non-Results or malformed JSON.</summary>
    public static DeepgramResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "Results") return null;
            if (!root.TryGetProperty("channel", out var channel)) return null;
            if (!channel.TryGetProperty("alternatives", out var alts)
                || alts.ValueKind != JsonValueKind.Array || alts.GetArrayLength() == 0) return null;

            var alt = alts[0];
            var transcript = alt.TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
            var confidence = alt.TryGetProperty("confidence", out var c) ? c.GetSingle() : 0f;
            var speechFinal = root.TryGetProperty("speech_final", out var sf) && sf.GetBoolean();
            var start = root.TryGetProperty("start", out var s) ? s.GetDouble() : 0d;
            return new DeepgramResult(transcript, confidence, speechFinal, start);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
