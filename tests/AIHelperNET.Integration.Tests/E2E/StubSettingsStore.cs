using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>Deterministic <see cref="ISettingsStore"/> that avoids file/secret IO.</summary>
public sealed class StubSettingsStore : ISettingsStore
{
    private readonly AppSettingsDto _settings = new(
        ActiveBackend: AiBackend.Claude,
        WhisperModel: WhisperModelSize.Base,
        AnswerSettings: AnswerSettings.Default,
        CodeProfile: CodeProfile.Empty,
        MicDeviceId: null,
        LoopbackDeviceId: null);

    /// <inheritdoc/>
    public Task<AppSettingsDto> LoadAsync(CancellationToken ct) => Task.FromResult(_settings);

    /// <inheritdoc/>
    public Task SaveAsync(AppSettingsDto settings, CancellationToken ct) => Task.CompletedTask;
}
