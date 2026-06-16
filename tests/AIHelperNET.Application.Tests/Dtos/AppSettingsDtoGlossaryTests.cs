using AIHelperNET.Application.Sessions.Dtos;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Dtos;

public class AppSettingsDtoGlossaryTests
{
    private static AppSettingsDto Make(string[] domains, bool enabled = true) =>
        new(default, default, null!, null!, null, null) { EnabledGlossaryDomains = domains, GlossaryEnabled = enabled };

    [Fact]
    public void Normalized_lowercases_and_dedupes_domains()
    {
        var n = Make(["DotNet", "dotnet", "AZURE"]).Normalized();
        n.EnabledGlossaryDomains.Should().BeEquivalentTo(["dotnet", "azure"]);
    }

    [Fact]
    public void Normalized_drops_blank_domains()
    {
        var n = Make(["dotnet", "", "  "]).Normalized();
        n.EnabledGlossaryDomains.Should().BeEquivalentTo(["dotnet"]);
    }

    [Fact]
    public void Default_glossary_enabled_is_true()
    {
        var dto = new AppSettingsDto(default, default, null!, null!, null, null);
        dto.GlossaryEnabled.Should().BeTrue();
    }
}
