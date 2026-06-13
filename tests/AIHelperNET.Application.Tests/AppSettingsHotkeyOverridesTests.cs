using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class AppSettingsHotkeyOverridesTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static AppSettingsDto Base() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public void HotkeyOverrides_RoundTripThroughJson()
    {
        var dto = Base() with
        {
            HotkeyOverrides = [new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G)]
        };

        var back = JsonSerializer.Deserialize<AppSettingsDto>(JsonSerializer.Serialize(dto, Web), Web)!;

        back.HotkeyOverrides.Should().ContainSingle()
            .Which.Should().Be(new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G));
    }

    [Fact]
    public void Normalized_DropsInvalidEnumAndDuplicateIdOverrides()
    {
        var dto = Base() with
        {
            HotkeyOverrides =
            [
                new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.G),
                new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.J), // dup Id → dropped
                new HotkeyOverride((HotkeyId)999, ModifierKeys.Ctrl, VirtualKey.G),            // bad Id → dropped
                new HotkeyOverride(HotkeyId.CopyAnswer, (ModifierKeys)0x40, VirtualKey.G),     // bad modifier bit → dropped
                new HotkeyOverride(HotkeyId.ToggleOverlay, ModifierKeys.Ctrl, (VirtualKey)0x07) // bad key → dropped
            ]
        };

        var n = dto.Normalized();

        n.HotkeyOverrides.Should().ContainSingle()
            .Which.Should().Be(new HotkeyOverride(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl, VirtualKey.G));
    }

    [Fact]
    public void MissingField_DefaultsToEmpty()
    {
        const string json = """{"activeBackend":0,"whisperModel":2,"micDeviceId":null,"loopbackDeviceId":null}""";
        var dto = JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!;
        dto.HotkeyOverrides.Should().BeEmpty();
    }
}
