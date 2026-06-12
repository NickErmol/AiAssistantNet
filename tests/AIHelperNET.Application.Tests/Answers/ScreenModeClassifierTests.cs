using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenModeClassifierTests
{
    [Theory]
    [InlineData("Can you write a SQL to get the students")]
    [InlineData("write a query that returns the top 5")]
    [InlineData("Now implement an LRU cache")]
    [InlineData("solve this task in 20 minutes")]
    [InlineData("write a function that reverses a string")]
    public void Classify_CodingPhrases_ReturnsSolveCodingTask(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.SolveCodingTask);

    [Theory]
    [InlineData("can you fix the bug in this method")]
    [InlineData("why is this failing when I run it")]
    [InlineData("what's wrong with this code")]
    public void Classify_DebugPhrases_ReturnsDebugError(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.DebugError);

    [Theory]
    [InlineData("explain this code to me")]
    [InlineData("what does this code do")]
    [InlineData("walk me through this code")]
    public void Classify_ExplainPhrases_ReturnsExplainCode(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.ExplainCode);

    [Theory]
    [InlineData("design a system for a URL shortener")]
    [InlineData("how would you design a rate limiter")]
    public void Classify_DesignPhrases_ReturnsSystemDesign(string text)
        => ScreenModeClassifier.Classify(text).Should().Be(ScreenAnalysisMode.SystemDesign);

    [Fact]
    public void Classify_FixTheCode_PrefersDebugOverCoding()
        => ScreenModeClassifier.Classify("can you fix the code here")
            .Should().Be(ScreenAnalysisMode.DebugError);

    [Fact]
    public void Classify_ExplainTheCode_PrefersExplainOverCoding()
        => ScreenModeClassifier.Classify("explain the code on screen")
            .Should().Be(ScreenAnalysisMode.ExplainCode);

    [Fact]
    public void Classify_IsCaseInsensitive()
        => ScreenModeClassifier.Classify("WRITE A SQL QUERY")
            .Should().Be(ScreenAnalysisMode.SolveCodingTask);

    [Theory]
    [InlineData("tell me about your experience with databases")]
    [InlineData("what is a primary key")]
    [InlineData("")]
    [InlineData("   ")]
    public void Classify_NonTaskOrEmpty_ReturnsNull(string text)
        => ScreenModeClassifier.Classify(text).Should().BeNull();
}
