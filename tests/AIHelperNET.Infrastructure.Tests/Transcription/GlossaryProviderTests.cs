using AIHelperNET.Infrastructure.Transcription;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class GlossaryProviderTests
{
    private static readonly JsonTranscriptionGlossaryProvider Provider = new();

    [Fact]
    public void Loads_all_nine_domains()
    {
        Provider.Domains.Select(d => d.Key).Should().BeEquivalentTo(
            ["dotnet", "angular", "react", "sql", "mssql", "postgres", "azure", "aws", "cloud"]);
    }

    [Fact]
    public void Every_domain_has_core_and_terms()
    {
        foreach (var d in Provider.Domains)
        {
            d.DisplayName.Should().NotBeNullOrWhiteSpace();
            d.Core.Should().HaveCountGreaterThanOrEqualTo(3).And.HaveCountLessThanOrEqualTo(6);
            d.Terms.Should().HaveCountGreaterThanOrEqualTo(20);
            d.Terms.Should().OnlyHaveUniqueItems();
        }
    }

    [Fact]
    public void BuildPromptSuffix_includes_enabled_core()
    {
        var suffix = Provider.BuildPromptSuffix(new HashSet<string> { "azure" }, "", 100);
        suffix.Should().Contain("Key Vault");
    }

    [Fact]
    public void BuildPromptSuffix_empty_when_disabled()
    {
        Provider.BuildPromptSuffix(new HashSet<string>(), "anything", 100).Should().BeEmpty();
    }
}
