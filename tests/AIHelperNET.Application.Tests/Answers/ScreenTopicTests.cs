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

    [Fact]
    public void Derive_StartsAtTaskMarker_DroppingViewerChrome()
    {
        // The capture OCRs the whole foreground window, so the first line leaks viewer chrome
        // (toolbar words + the image filename) before the real task.
        var ocr = "Edit  e  coding_question.png  Question: Implement a binary search algorithm";
        ScreenTopic.Derive(ocr).Should().Be("Question: Implement a binary search algorithm");
    }

    [Fact]
    public void Derive_StripsLeadingImageFilenameToken()
    {
        var ocr = "screenshot_2024.png Implement an LRU cache";
        ScreenTopic.Derive(ocr).Should().Be("Implement an LRU cache");
    }
}
