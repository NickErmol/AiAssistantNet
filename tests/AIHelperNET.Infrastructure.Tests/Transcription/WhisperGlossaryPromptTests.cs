using AIHelperNET.Infrastructure.Transcription;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class WhisperGlossaryPromptTests
{
    [Fact]
    public void Suffix_surfaces_garble_prone_terms_within_budget()
    {
        var provider = new JsonTranscriptionGlossaryProvider();
        var enabled = new HashSet<string> { "dotnet", "azure" };
        var suffix = provider.BuildPromptSuffix(enabled, recentContext: "tell me about loading and the vault", wordBudget: 110);

        suffix.Should().NotBeEmpty();
        suffix.Split(' ').Length.Should().BeLessThanOrEqualTo(130); // budget + joiners
        suffix.Should().ContainAny("Key Vault", "Entity Framework"); // a core term made it in
    }
}
