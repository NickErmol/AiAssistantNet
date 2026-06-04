using AIHelperNET.App.Services;
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.App;

/// <summary>Registers all presentation-layer services.</summary>
public static class DependencyInjection
{
    /// <summary>Adds presentation services to the service collection.</summary>
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        // Sinks — singleton so the same instance is used by both infrastructure and ViewModels
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());
        services.AddSingleton<TranscriptSink>();
        services.AddSingleton<ITranscriptSink>(sp => sp.GetRequiredService<TranscriptSink>());
        services.AddSingleton<ConversationTurnSinkAdapter>();
        services.AddSingleton<IConversationTurnSink>(sp => sp.GetRequiredService<ConversationTurnSinkAdapter>());

        // Session pipeline runner
        services.AddSingleton<SessionRunner>();

        // ViewModels
        services.AddSingleton<SessionControlViewModel>();
        services.AddSingleton<TranscriptViewModel>();
        services.AddSingleton<ConversationTurnViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<AudioLevelViewModel>();

        // Window context + windows
        services.AddSingleton<MainOverlayWindowContext>();
        services.AddSingleton<MainOverlayWindow>();
        services.AddSingleton<SettingsWindow>();

        return services;
    }
}
