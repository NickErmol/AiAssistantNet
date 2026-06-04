using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Serialisable application settings shared between the UI and the settings store.</summary>
/// <param name="ActiveBackend">Selected AI backend.</param>
/// <param name="WhisperModel">Whisper model size used for transcription.</param>
/// <param name="AnswerSettings">Answer style settings.</param>
/// <param name="CodeProfile">Active tech-stack profile injected into prompts.</param>
/// <param name="MicDeviceId">NAudio device ID for the microphone input, or null for the system default.</param>
/// <param name="LoopbackDeviceId">NAudio device ID for loopback capture, or null for the system default.</param>
/// <param name="AnswerFontSize">Font size in points for the answer display area.</param>
/// <param name="WhisperLanguage">Whisper transcription language code, or "auto" for auto-detection.</param>
/// <param name="OverlayOpacity">Overlay window opacity in range [0.2, 1.0].</param>
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
