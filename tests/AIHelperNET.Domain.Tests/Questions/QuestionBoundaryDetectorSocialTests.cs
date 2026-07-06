using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Questions;

/// <summary>
/// Social/personal questions from the interviewer ("what injury do you have?", "how are you?") are
/// grammatically complete questions, so the plain question rules fire QuestionComplete at 0.85 and
/// an answer card is generated — in the 2026-07-06 live session six such cards were wasted on
/// injury chat and greetings. The heuristic must instead emit LOW confidence (&lt; 0.7) for them so
/// the AI classifier decides. It must never hard-drop: only defer.
/// </summary>
public sealed class QuestionBoundaryDetectorSocialTests
{
    private readonly QuestionBoundaryDetector _sut = new();
    private static readonly IReadOnlyList<string> NoRecent = [];

    // Verbatim social questions from the 2026-07-06 session that each burned a generation.
    [Theory]
    [InlineData("Now what injury do you have?")]
    [InlineData("Hey, hey, hey, hey, Kumar, how are you?")]
    public void RealSessionSocialQuestions_DeferToAiInsteadOfGenerating(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecent);

        result.ShouldGenerateAnswer.Should().BeFalse("social small talk must not fire a generation directly");
        result.Confidence.Should().BeLessThan(0.7, "the AI classifier gets the final say");
    }

    [Theory]
    [InlineData("How are you doing today?")]
    [InlineData("Did you have a good weekend?")]
    [InlineData("How was your vacation, did you enjoy it?")]
    public void CommonSocialQuestions_DeferToAiInsteadOfGenerating(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecent);

        result.ShouldGenerateAnswer.Should().BeFalse();
        result.Confidence.Should().BeLessThan(0.7);
    }

    // Technical questions must be completely unaffected — same label and confidence as before.
    // "How was your experience with..." is a canonical experience-question phrasing and must NOT
    // be caught by the greeting list (reviewer-flagged false positive).
    [Theory]
    [InlineData("Can you explain the N+1 query problem in Entity Framework Core?")]
    [InlineData("What kind of OAuth flows have you worked on?")]
    [InlineData("How was your experience with Azure API Management?")]
    public void TechnicalQuestions_StillFireQuestionCompleteAtFullConfidence(string text)
    {
        var result = _sut.Evaluate(text, Speaker.Other, null, NoRecent);

        result.Classification.Should().Be(BoundaryLabel.QuestionComplete);
        result.Confidence.Should().Be(0.85);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }

    [Fact]
    public void ImperativeTask_StillFiresTaskComplete()
    {
        var result = _sut.Evaluate("Explain the DDD concept.", Speaker.Other, null, NoRecent);

        result.Classification.Should().Be(BoundaryLabel.TaskComplete);
        result.ShouldGenerateAnswer.Should().BeTrue();
    }
}
