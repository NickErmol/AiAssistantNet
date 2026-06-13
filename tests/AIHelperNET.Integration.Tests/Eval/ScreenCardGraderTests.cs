using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class ScreenCardGraderTests
{
    [Theory]
    [InlineData("here is the answer\n```sql\nSELECT 1\n```", true)]
    [InlineData("```CSharp\nvar x = 1;\n```", true)]
    [InlineData("no code here, just prose.", false)]
    public void HasFencedCode_DetectsFenceRegardlessOfLanguage(string text, bool expected)
        => ScreenCardGrader.HasFencedCode(text).Should().Be(expected);

    [Fact]
    public void MissingSubstrings_IsCaseInsensitive_AndReturnsOnlyAbsent()
    {
        ScreenCardGrader.MissingSubstrings("SELECT distinct name FROM t", ["DISTINCT"])
            .Should().BeEmpty();
        ScreenCardGrader.MissingSubstrings("SELECT name FROM t", ["DISTINCT", "WHERE"])
            .Should().BeEquivalentTo(["DISTINCT", "WHERE"]);
    }

    [Fact]
    public void ParseJudgeVerdict_StripsJsonFence_AndParses()
    {
        const string raw = "```json\n{ \"verdict\": \"PASS\", \"score\": 1, \"reason\": \"correct\" }\n```";
        var v = ScreenCardGrader.ParseJudgeVerdict(raw);
        v.Verdict.Should().Be("PASS");
        v.Score.Should().Be(1.0);
        v.Reason.Should().Be("correct");
    }

    [Fact]
    public void ParseJudgeVerdict_PlainJson_Parses()
    {
        const string raw = "{ \"verdict\": \"PARTIAL\", \"score\": 0.5, \"reason\": \"missing edge case\" }";
        var v = ScreenCardGrader.ParseJudgeVerdict(raw);
        v.Verdict.Should().Be("PARTIAL");
        v.Score.Should().Be(0.5);
    }

    [Fact]
    public void ParseJudgeVerdict_Unparseable_ReturnsZeroScoreErrorVerdict()
    {
        var v = ScreenCardGrader.ParseJudgeVerdict("not json at all");
        v.Score.Should().Be(0.0);
        v.Verdict.Should().Be("UNPARSEABLE");
    }
}
