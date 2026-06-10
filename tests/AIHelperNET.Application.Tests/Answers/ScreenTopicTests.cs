using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenTopicTests
{
    [Fact]
    public void Derive_UsesFirstNonEmptyLine_Trimmed()
    {
        var ocr = "\n   Implement an LRU cache   \nwith O(1) get and put\n";
        ScreenTopic.Derive(ocr).Should().Be("Implement an LRU cache");
    }

    [Fact]
    public void Derive_CapsLongLineAt120Chars()
    {
        var ocr = new string('x', 200);
        ScreenTopic.Derive(ocr).Should().HaveLength(120);
    }

    [Fact]
    public void Derive_EmptyOrWhitespace_ReturnsFallback()
    {
        ScreenTopic.Derive("   ").Should().Be("Screen task");
        ScreenTopic.Derive("").Should().Be("Screen task");
    }
}
