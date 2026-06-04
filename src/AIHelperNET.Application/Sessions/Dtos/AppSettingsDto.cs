using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Serialisable application settings shared between the UI and the settings store.</summary>
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId,
    int AnswerFontSize = 12,
    string WhisperLanguage = "auto",
    double OverlayOpacity = 0.75)
{
    /// <summary>Named setting presets for quick profile switching.</summary>
    public IReadOnlyList<ProfilePreset> Presets { get; init; } = [];
}
