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
}
