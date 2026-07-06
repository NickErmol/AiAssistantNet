using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>
/// <see cref="ScreenOcrChromeFilter"/> must reject captures that are meeting/window UI chrome and
/// accept real task content. The chrome samples are verbatim OCR from the 2026-07-06 live session,
/// where a captured Teams title bar created three garbage screen turns and (as the task in focus)
/// misrouted every subsequent interviewer question for 19 minutes.
/// </summary>
public class ScreenOcrChromeFilterTests
{
    // --- chrome: must be rejected -------------------------------------------------------------

    [Theory]
    [InlineData("Introduction with Mikalai, Vention X Apollo 0 24:05 .11 Mikalai Yarmolenkma tl•dV A1 NOTETAKER Vention Notetaker KS KS K")]
    [InlineData("Introduction with Mikalai, Vention X Apollo 0 15:29 tl•dV A1 NOTETAKER Vention Notetaker KS Chat 0 People Raise React Vi")]
    [InlineData("Introduction with Mikalai, Vention X Apollo 0 21:11 .11 Mikalai YarrnolenkmJ tl•dV A1 NOTETAKER Vention Notetaker KS KS")]
    public void RealTeamsTitleBarCaptures_AreChrome(string ocr)
        => ScreenOcrChromeFilter.IsLikelyChrome(ocr).Should().BeTrue();

    [Fact]
    public void ZoomStyleCallControls_AreChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Mute Stop Video Participants Chat Share Screen Reactions Leave 12:05")
            .Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  ")]
    public void EmptyOrWhitespace_IsChrome(string? ocr)
        => ScreenOcrChromeFilter.IsLikelyChrome(ocr).Should().BeTrue();

    // --- real content: must pass through ------------------------------------------------------

    [Fact]
    public void CodingTaskWithCode_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Implement an LRU cache in C#\npublic class LruCache<TKey, TValue> {\n    private readonly int _capacity;\n}")
            .Should().BeFalse();

    [Fact]
    public void QuestionText_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Can you write a SQL query to find the second highest salary?")
            .Should().BeFalse();

    [Fact]
    public void TaskMarkerLine_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome("Task: reverse a linked list in place")
            .Should().BeFalse();

    [Fact]
    public void SystemDesignPromptMentioningChatAndPeople_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Design a chat application for people across regions")
            .Should().BeFalse();

    [Fact]
    public void ReactComponentTask_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Build a React component that fetches and renders a paginated user list")
            .Should().BeFalse();

    // Prose tasks stacked with meeting-adjacent vocabulary (React, mute, participants, camera) are
    // canonical frontend/WebRTC interview subjects — the filter must key on chrome SHAPE
    // (title-case control strips, roster shrapnel, clocks), not vocabulary alone.
    [Fact]
    public void ReactTaskMentioningMuteAndParticipants_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Build a React component that allows users to mute audio and shows a list of participants")
            .Should().BeFalse();

    [Fact]
    public void VideoChatFeatureTask_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "Implement a video chat feature that lets users toggle their camera on and off and mute their microphone. Include a participants list.")
            .Should().BeFalse();

    [Fact]
    public void ProseMentioningAClockTime_IsNotChrome()
        => ScreenOcrChromeFilter.IsLikelyChrome(
                "The nightly job runs at 02:30 and reconciles all pending invoices")
            .Should().BeFalse();

    [Fact]
    public void LongTaskText_IsNotChrome()
    {
        var longTask = string.Join(' ', Enumerable.Repeat(
            "Given a stream of events design a pipeline that deduplicates and aggregates them", 6));
        ScreenOcrChromeFilter.IsLikelyChrome(longTask).Should().BeFalse();
    }
}
