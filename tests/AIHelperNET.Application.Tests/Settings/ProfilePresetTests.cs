using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using Xunit;

namespace AIHelperNET.Application.Tests.Settings;

public sealed class ProfilePresetTests
{
    [Fact]
    public void ProfilePreset_RoundTripsViaJsonSerializer()
    {
        var preset = new ProfilePreset(
            "C# Azure",
            new CodeProfile("C#", "ASP.NET Core", "Angular", "SQL Server",
                "Azure", null, "Clean", "xUnit", null),
            AnswerSettings.Default);

        var json = System.Text.Json.JsonSerializer.Serialize(preset);
        var restored = System.Text.Json.JsonSerializer.Deserialize<ProfilePreset>(json);

        Assert.NotNull(restored);
        Assert.Equal("C# Azure", restored.Name);
        Assert.Equal("C#", restored.CodeProfile.ProgrammingLanguage);
        Assert.Equal(AnswerSettings.Default, restored.AnswerSettings);
    }

    [Fact]
    public void AppSettingsDto_HasDefaultWhisperLanguage()
    {
        var dto = new AppSettingsDto(
            AiBackend.Claude,
            WhisperModelSize.Base,
            AnswerSettings.Default,
            CodeProfile.Empty,
            null, null);

        Assert.Equal("auto", dto.WhisperLanguage);
        Assert.Equal(0.75, dto.OverlayOpacity);
        Assert.Empty(dto.Presets);
    }
}
