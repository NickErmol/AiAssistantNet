using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class PcmConverterTests
{
    [Fact]
    public void ToLinear16_ConvertsAndClamps()
    {
        var bytes = PcmConverter.ToLinear16([0f, 1f, -1f, 2f, 0.5f]);

        bytes.Should().HaveCount(10);
        BitConverter.ToInt16(bytes, 0).Should().Be(0);
        BitConverter.ToInt16(bytes, 2).Should().Be(short.MaxValue);         // 1.0 → max
        BitConverter.ToInt16(bytes, 4).Should().Be(-short.MaxValue);        // -1.0 → -max (symmetric)
        BitConverter.ToInt16(bytes, 6).Should().Be(short.MaxValue);         // clamped
        BitConverter.ToInt16(bytes, 8).Should().Be((short)Math.Round(0.5f * short.MaxValue));
    }
}
