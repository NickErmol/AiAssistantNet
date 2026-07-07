using System.Text.Json;
using System.Text.Json.Nodes;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

/// <summary>Validates the real settings.json persistence path for <c>MaxAnswerTokens</c>, using the same
/// <see cref="JsonSerializerDefaults.Web"/> options as <c>JsonSettingsStore</c>.</summary>
public class AppSettingsJsonTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AppSettingsDto Sample(int tokens) => new AppSettingsDto(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null)
        with { MaxAnswerTokens = tokens };

    private static AppSettingsDto BaseDto() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public void LegacyJson_MissingMaxAnswerTokens_CoercesToDefault()
    {
        // Serialize a real dto, then strip the field to simulate a settings.json written before
        // MaxAnswerTokens existed. This sidesteps enum-encoding assumptions in hand-written JSON.
        var json = JsonSerializer.Serialize(Sample(1234), Web);
        var node = JsonNode.Parse(json)!.AsObject();
        node.Remove("maxAnswerTokens"); // Web options use camelCase
        var legacyJson = node.ToJsonString();

        var dto = JsonSerializer.Deserialize<AppSettingsDto>(legacyJson, Web)!.Normalized();

        dto.MaxAnswerTokens.Should().Be(800);
    }

    [Fact]
    public void RoundTrip_PreservesInRangeMaxAnswerTokens()
    {
        var json = JsonSerializer.Serialize(Sample(1500), Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!.Normalized();

        restored.MaxAnswerTokens.Should().Be(1500);
    }

    // ─── Profile fields round-trip ────────────────────────────────────────────

    [Fact]
    public void RoundTrip_ResumeRawText_PlainString()
    {
        var dto = BaseDto() with { ResumeRawText = "Senior C# developer at Contoso, 2018-2024." };
        var json = JsonSerializer.Serialize(dto, Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;

        restored.ResumeRawText.Should().Be(dto.ResumeRawText);
    }

    [Fact]
    public void RoundTrip_ResumeRawText_MultilineWithMarkdown()
    {
        var multiline = "# John Smith\n\n**Experience**\n- Contoso Corp — Senior Engineer (3 years)\n  * Led Azure migration\n  * Mentored 5 devs\n\n**Skills:** C#, .NET, Azure, SQL Server";
        var dto = BaseDto() with { ResumeRawText = multiline };
        var json = JsonSerializer.Serialize(dto, Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;

        restored.ResumeRawText.Should().Be(multiline);
    }

    [Fact]
    public void RoundTrip_JobDescriptionRawText_MultilineWithMarkdown()
    {
        var jd = "## Senior .NET Engineer\n\n**Requirements:**\n- 5+ years C#/.NET\n- Azure experience\n- Microservices\n\n**Nice to have:** Kubernetes, Terraform";
        var dto = BaseDto() with { JobDescriptionRawText = jd };
        var json = JsonSerializer.Serialize(dto, Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;

        restored.JobDescriptionRawText.Should().Be(jd);
    }

    [Fact]
    public void RoundTrip_CandidateProfileCard_MultilineMarkdown()
    {
        var card = "**CANDIDATE PROFILE**\nSenior .NET engineer with 6 years in cloud-native systems.\n- Contoso — Senior Engineer (3 yrs)\n- Fabrikam — Mid Engineer (2 yrs)\n\n**Skills:** C#, Azure, microservices";
        var dto = BaseDto() with { CandidateProfileCard = card };
        var json = JsonSerializer.Serialize(dto, Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;

        restored.CandidateProfileCard.Should().Be(card);
    }

    [Fact]
    public void RoundTrip_AllThreeProfileFields_SetTogether()
    {
        var dto = BaseDto() with
        {
            ResumeRawText = "My resume text\nwith multiple lines",
            JobDescriptionRawText = "Job description\nwith requirements",
            CandidateProfileCard = "**CANDIDATE PROFILE**\nCondensed card content"
        };
        var json = JsonSerializer.Serialize(dto, Web);
        var restored = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;

        restored.ResumeRawText.Should().Be(dto.ResumeRawText);
        restored.JobDescriptionRawText.Should().Be(dto.JobDescriptionRawText);
        restored.CandidateProfileCard.Should().Be(dto.CandidateProfileCard);
    }

    [Fact]
    public void AbsentProfileFields_DefaultToNull()
    {
        // Simulate a settings.json that was written before the profile fields existed
        var json = JsonSerializer.Serialize(BaseDto(), Web);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        node.Remove("resumeRawText");
        node.Remove("jobDescriptionRawText");
        node.Remove("candidateProfileCard");
        var legacyJson = node.ToJsonString();

        var restored = JsonSerializer.Deserialize<AppSettingsDto>(legacyJson, Web)!;

        restored.ResumeRawText.Should().BeNull();
        restored.JobDescriptionRawText.Should().BeNull();
        restored.CandidateProfileCard.Should().BeNull();
    }

    [Fact]
    public void Default_ProfileFields_AreNull()
    {
        var dto = BaseDto();

        dto.ResumeRawText.Should().BeNull();
        dto.JobDescriptionRawText.Should().BeNull();
        dto.CandidateProfileCard.Should().BeNull();
    }
}
