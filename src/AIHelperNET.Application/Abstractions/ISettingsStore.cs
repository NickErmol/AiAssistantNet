using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for loading and saving application settings.</summary>
public interface ISettingsStore
{
    /// <summary>Loads current settings from the backing store.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<AppSettingsDto> LoadAsync(CancellationToken ct);

    /// <summary>Persists the given settings to the backing store.</summary>
    /// <param name="settings">Settings to save.</param>
    /// <param name="ct">Cancellation token.</param>
    Task SaveAsync(AppSettingsDto settings, CancellationToken ct);
}
