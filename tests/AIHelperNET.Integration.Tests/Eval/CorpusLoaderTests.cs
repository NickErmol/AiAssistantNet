using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class CorpusLoaderTests
{
    [Fact]
    public void Load_ReturnsNonEmptyCorpus_WithParsedEnums()
    {
        var entries = CorpusLoader.Load();

        entries.Should().NotBeEmpty();
        entries.Should().OnlyHaveUniqueItems(e => e.Id);
        entries.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.LatestItem.Text));
        entries.Select(e => e.ExpectedLabel).Distinct().Count().Should().BeGreaterThanOrEqualTo(6);
    }

    [Fact]
    public void Load_CoversTheKeyScenarioCases()
    {
        var entries = CorpusLoader.Load();
        entries.Should().Contain(e => e.Id == "scenarioA-cache-invalidation"
            && e.ExpectedLabel == BoundaryLabel.QuestionContinued);
        entries.Should().Contain(e => e.Id == "scenarioB-different-topic"
            && e.ExpectedLabel == BoundaryLabel.NewQuestion);
    }
}
