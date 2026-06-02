using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.App;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services)
    {
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());

        services.AddSingleton<OverlayViewModel>();
        services.AddSingleton<SettingsViewModel>();

        services.AddTransient<OverlayWindow>();
        services.AddTransient<SettingsWindow>();

        return services;
    }
}
