using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class ScreenAnswerCorpusLoaderTests
{
    [Fact]
    public void Load_ReturnsAllScenarios_WithParsedModesAndFollowUp()
    {
        var corpus = ScreenAnswerCorpusLoader.Load();

        corpus.Should().HaveCount(10);
        corpus.Select(s => s.Id).Should().Contain(
            ["sql-student-location-access", "csharp-super-manager", "angular-counter-component"]);
        corpus.Select(s => s.Id).Should().NotContain("chitchat-no-task",
            "the chit-chat control was dropped — it exercised a General screen-mode path that never " +
            "fires for non-screen-task chit-chat in production");

        var sql = corpus.Single(s => s.Id == "sql-student-location-access");
        sql.Mode.Should().Be(ScreenAnalysisMode.SolveCodingTask);
        sql.FollowUp.Should().NotBeNull();
        sql.FollowUp!.RequiredSubstrings.Should().Contain("DISTINCT");

        corpus.Count(s => s.FollowUp is not null).Should().Be(1);
    }
}
