using System.Buffers.Binary;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>Converts [-1,1] float PCM to 16-bit little-endian PCM (linear16), clamping out-of-range samples.</summary>
public static class PcmConverter
{
    /// <summary>Converts float samples to linear16 bytes (2 bytes per sample, little-endian).</summary>
    public static byte[] ToLinear16(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)MathF.Round(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }
        return bytes;
    }
}
