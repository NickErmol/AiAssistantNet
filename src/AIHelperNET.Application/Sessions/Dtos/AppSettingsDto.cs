using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Serialisable application settings shared between the UI and the settings store.</summary>
/// <param name="ActiveBackend">Which AI backend to use.</param>
/// <param name="WhisperModel">Whisper model size for transcription.</param>
/// <param name="AnswerSettings">Answer generation settings.</param>
/// <param name="CodeProfile">Developer code profile.</param>
/// <param name="MicDeviceId">Microphone device ID, or null for system default.</param>
/// <param name="LoopbackDeviceId">Loopback device ID, or null for system default.</param>
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId);
