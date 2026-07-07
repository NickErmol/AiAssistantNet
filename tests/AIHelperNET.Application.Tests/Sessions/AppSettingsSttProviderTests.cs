using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class AppSettingsSttProviderTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_LegacySettingsWithoutSttProvider_DefaultsToWhisper()
    {
        var dto = JsonSerializer.Deserialize<AppSettingsDto>("{}", Web)!;
        dto.SttProvider.Should().Be(SttProvider.Whisper);
    }

    [Fact]
    public void Roundtrip_PreservesSttProvider()
    {
        var original = JsonSerializer.Deserialize<AppSettingsDto>("{}", Web)! with
        {
            SttProvider = SttProvider.Deepgram
        };
        var json = JsonSerializer.Serialize(original, Web);
        JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!
            .SttProvider.Should().Be(SttProvider.Deepgram);
    }
}
