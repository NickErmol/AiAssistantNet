using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Domain.Tests.Questions;

public class QuestionDetectorTests
{
    private readonly QuestionDetector _sut = new();

    [Theory]
    [InlineData("What is dependency injection?")]
    [InlineData("Explain the SOLID principles")]
    [InlineData("How would you optimize this query")]
    [InlineData("Can you describe CQRS")]
    [InlineData("Implement a binary search")]
    [InlineData("Design a rate limiter")]
    public void Evaluate_QuestionText_DetectsAsQuestion(string text)
    {
        var result = _sut.Evaluate(text, []);
        result.IsQuestion.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
        result.NormalizedText.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [InlineData("I have ten years of experience.")]
    [InlineData("Let me share my background.")]
    [InlineData("Sure, I can do that.")]
    public void Evaluate_Statement_NotAQuestion(string text)
    {
        var result = _sut.Evaluate(text, []);
        result.IsQuestion.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_EmptyText_NotAQuestion()
    {
        _sut.Evaluate("", []).IsQuestion.Should().BeFalse();
        _sut.Evaluate("   ", []).IsQuestion.Should().BeFalse();
    }

    [Fact]
    public void Evaluate_NearDuplicate_MarkedDuplicate()
    {
        var recent = new[] { "What is dependency injection in dotnet?" };
        var result = _sut.Evaluate("What is dependency injection in dotnet", recent);
        result.IsDuplicate.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_DifferentQuestion_NotDuplicate()
    {
        var recent = new[] { "What is dependency injection?" };
        var result = _sut.Evaluate("How does garbage collection work?", recent);
        result.IsQuestion.Should().BeTrue();
        result.IsDuplicate.Should().BeFalse();
    }

    [Fact]
    public void Jaccard_IdenticalSets_ReturnsOne()
    {
        var a = new HashSet<string> { "a", "b", "c" };
        QuestionDetector.Jaccard(a, a).Should().Be(1.0);
    }

    [Fact]
    public void Jaccard_DisjointSets_ReturnsZero()
    {
        QuestionDetector.Jaccard(
            new HashSet<string> { "a" },
            new HashSet<string> { "b" }).Should().Be(0.0);
    }

    [Fact]
    public void Jaccard_BothEmpty_ReturnsOne()
    {
        QuestionDetector.Jaccard(
            new HashSet<string>(),
            new HashSet<string>()).Should().Be(1.0);
    }

    [Fact]
    public void Jaccard_PartialOverlap_CorrectValue()
    {
        // {a,b} ∩ {b,c} = {b}, union = {a,b,c} → 1/3
        QuestionDetector.Jaccard(
            new HashSet<string> { "a", "b" },
            new HashSet<string> { "b", "c" }).Should().BeApproximately(1.0 / 3.0, 0.001);
    }
}
