using AIHelperNET.Infrastructure.AI;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public class LatestQuestionExtractorTests
{
    [Fact]
    public void ParseResult_ReadsFoundQuestionAndContext()
    {
        // The Anthropic envelope: content[0].text holds the model's JSON.
        var envelope = """
        {"content":[{"type":"text","text":"{\"found\":true,\"question\":\"How would you design a rate limiter?\",\"context\":\"Discussing distributed APIs.\"}"}]}
        """;
        var result = LatestQuestionExtractor.ParseResult(envelope);
        result.Found.Should().BeTrue();
        result.QuestionText.Should().Be("How would you design a rate limiter?");
        result.ContextSummary.Should().Be("Discussing distributed APIs.");
    }

    [Fact]
    public void ParseResult_StripsJsonCodeFence()
    {
        var envelope = """
        {"content":[{"type":"text","text":"```json\n{\"found\":true,\"question\":\"Q?\",\"context\":\"\"}\n```"}]}
        """;
        LatestQuestionExtractor.ParseResult(envelope).QuestionText.Should().Be("Q?");
    }

    [Fact]
    public void ParseResult_NotFound_WhenModelSaysSo()
    {
        var envelope = """
        {"content":[{"type":"text","text":"{\"found\":false,\"question\":\"\",\"context\":\"\"}"}]}
        """;
        LatestQuestionExtractor.ParseResult(envelope).Found.Should().BeFalse();
    }

    [Fact]
    public void ParseResult_NotFound_OnMalformedOutput()
        => LatestQuestionExtractor.ParseResult("not json at all").Found.Should().BeFalse();
}
