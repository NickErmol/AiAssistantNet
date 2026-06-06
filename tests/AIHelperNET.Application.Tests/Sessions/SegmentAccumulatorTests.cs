using AIHelperNET.Application.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SegmentAccumulatorTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Add_SingleSegment_ReturnsNull_BuffersIt()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Hello there", T0).Should().BeNull();
    }

    [Fact]
    public void Add_TwoSegmentsWithin3s_ReturnsNull_BothBuffered()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Can we use them", T0).Should().BeNull();
        sut.Add("and what is the difference", T0.AddSeconds(1)).Should().BeNull();
    }

    [Fact]
    public void Add_ThirdSegmentBeyond3sGap_FlushesFirstTwo_StartsNewBuffer()
    {
        var sut = new SegmentAccumulator();
        sut.Add("Can we use them", T0);
        sut.Add("and what is the difference", T0.AddSeconds(1));

        var flushed = sut.Add("Next question", T0.AddSeconds(5));

        flushed.Should().Be("Can we use them and what is the difference");
    }

    [Fact]
    public void Add_SecondSegmentBeyond3sGap_FlushesFirst()
    {
        var sut = new SegmentAccumulator();
        sut.Add("First question", T0);

        var result = sut.Add("Second question", T0.AddSeconds(4));

        result.Should().Be("First question");
    }

    [Fact]
    public void Flush_EmptyBuffer_ReturnsNull()
    {
        var sut = new SegmentAccumulator();
        sut.Flush().Should().BeNull();
    }

    [Fact]
    public void Flush_NonEmptyBuffer_ReturnsCombinedText_ClearsBuffer()
    {
        var sut = new SegmentAccumulator();
        sut.Add("First", T0);
        sut.Add("Second", T0.AddSeconds(1));

        var result = sut.Flush();

        result.Should().Be("First Second");
        sut.Flush().Should().BeNull();
    }
}
