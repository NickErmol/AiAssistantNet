using AIHelperNET.Application.Sessions.Glossary;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Glossary;

public class GlossarySelectorTests
{
    private static readonly GlossaryDomain Dotnet = new(
        "dotnet", ".NET",
        Core: ["Entity Framework", "dependency injection"],
        Terms: ["AsNoTracking", "captive dependency", "MediatR", "Polly", "SemaphoreSlim"]);

    private static readonly GlossaryDomain Azure = new(
        "azure", "Azure",
        Core: ["Azure Key Vault"],
        Terms: ["managed identity", "Service Bus", "Cosmos DB"]);

    private static readonly IReadOnlyList<GlossaryDomain> All = [Dotnet, Azure];

    [Fact]
    public void Excludes_disabled_domains()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet" }, recentContext: "", wordBudget: 100);
        result.Should().Contain("Entity Framework");
        result.Should().NotContain("Azure Key Vault");
    }

    [Fact]
    public void Always_includes_core_when_domain_enabled()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 100);
        result.Should().Contain("Entity Framework").And.Contain("dependency injection").And.Contain("Azure Key Vault");
    }

    [Fact]
    public void Context_matching_terms_rank_above_unrelated()
    {
        var result = GlossarySelector.Select(
            All, new HashSet<string> { "dotnet" },
            recentContext: "we kept hitting a captive dependency problem with our singleton",
            wordBudget: 5);
        result.Should().Contain("captive dependency");
    }

    [Fact]
    public void Respects_word_budget()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 4);
        result.Sum(t => t.Split(' ').Length).Should().BeLessThanOrEqualTo(4);
    }

    [Fact]
    public void Empty_enabled_set_returns_empty()
    {
        GlossarySelector.Select(All, new HashSet<string>(), recentContext: "x", wordBudget: 100).Should().BeEmpty();
    }

    [Fact]
    public void No_duplicate_terms()
    {
        var result = GlossarySelector.Select(All, new HashSet<string> { "dotnet", "azure" }, recentContext: "", wordBudget: 100);
        result.Should().OnlyHaveUniqueItems();
    }
}
