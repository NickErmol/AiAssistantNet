using System.Net.Http;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Audio;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Export;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Ocr;
using AIHelperNET.Infrastructure.Persistence;
using AIHelperNET.Infrastructure.Security;
using AIHelperNET.Infrastructure.Transcription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OllamaSharp;

namespace AIHelperNET.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration config)
    {
        AppPaths.EnsureDirectoriesExist();

        // Persistence
        services.AddDbContext<AppDbContext>(o =>
            o.UseSqlite($"Data Source={AppPaths.DatabaseFile}",
                b => b.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();

        // Settings & secrets
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<ISecretStore, WindowsCredentialSecretStore>();

        // Export
        services.AddSingleton<IExportService, ExportService>();

        // Hotkeys
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // Audio & transcription
        services.AddHttpClient(nameof(WhisperModelProvider));
        services.AddSingleton<IAudioCaptureService, NAudioCaptureService>();
        services.AddSingleton<IAudioLevelMonitor, AudioLevelMonitor>();
        services.AddSingleton<WhisperModelProvider>();
        services.AddHttpClient(nameof(SileroModelProvider));
        services.AddSingleton<SileroModelProvider>();
        services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();

        // OCR
        services.AddSingleton<IScreenOcrService, WindowsOcrService>();

        // AI providers
        services.Configure<ClaudeOptions>(config.GetSection("Claude"));
        services.Configure<OllamaOptions>(config.GetSection("Ollama"));

        services.AddHttpClient<HaikuQuestionClassifier>();
        services.AddSingleton<IQuestionClassifier, HaikuQuestionClassifier>();

        services.AddHttpClient<QuestionBoundaryClassifier>();
        services.AddSingleton<IQuestionBoundaryClassifier, QuestionBoundaryClassifier>();

        services.AddHttpClient<ClaudeAnswerProvider>();
        services.AddSingleton<ClaudeAnswerProvider>();
        services.AddSingleton<IOllamaApiClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            return new OllamaApiClient(new Uri(opts.BaseUrl));
        });
        services.AddSingleton<OllamaAnswerProvider>();
        services.AddSingleton<IAnswerProviderResolver, AnswerProviderResolver>();

        return services;
    }
}
