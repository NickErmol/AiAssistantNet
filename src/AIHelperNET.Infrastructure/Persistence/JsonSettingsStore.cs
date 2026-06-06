using System.IO;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Infrastructure.Common;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public async Task<AppSettingsDto> LoadAsync(CancellationToken ct)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return DefaultSettings();

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<AppSettingsDto>(stream, Options, ct)
               ?? DefaultSettings();
    }

    public async Task SaveAsync(AppSettingsDto settings, CancellationToken ct)
    {
        AppPaths.EnsureDirectoriesExist();
        await using var stream = File.Create(AppPaths.SettingsFile);
        await JsonSerializer.SerializeAsync(stream, settings, Options, ct);
    }

    private static AppSettingsDto DefaultSettings() => new(
        AiBackend.Claude,
        WhisperModelSize.Medium,
        Domain.ValueObjects.AnswerSettings.Default,
        Domain.ValueObjects.CodeProfile.Empty,
        null,
        null);
}
