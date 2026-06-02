using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Audio;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using AIHelperNET.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        AppPaths.EnsureDirectoriesExist();

        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={AppPaths.DatabaseFile}"));

        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        services.AddHttpClient(nameof(Transcription.WhisperModelProvider));
        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
        services.AddSingleton<Transcription.WhisperModelProvider>();
        services.AddSingleton<ITranscriptionService, Transcription.WhisperTranscriptionService>();

        return services;
    }
}
