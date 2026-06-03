using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AIHelperNET.App;

/// <summary>Application entry point and host lifecycle coordinator.</summary>
public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureAIHelper();
        _host = builder.Build();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.EnsureCreatedAsync();
        }

        await _host.StartAsync();

        var overlay = _host.Services.GetRequiredService<MainOverlayWindow>();

        // Wire TranscriptSink → TranscriptViewModel
        var transcriptSink = _host.Services.GetRequiredService<TranscriptSink>();
        var transcriptVm   = _host.Services.GetRequiredService<TranscriptViewModel>();
        transcriptSink.SetHandler(item => transcriptVm.AddItem(item));

        // Wire AnswerStreamSink → ConversationTurnViewModel
        var answerSink = _host.Services.GetRequiredService<AnswerStreamSink>();
        var turnVm     = _host.Services.GetRequiredService<ConversationTurnViewModel>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => ConversationTurnViewModel.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));

        overlay.Show();
        WireHotkeys(overlay);
    }

    private void WireHotkeys(MainOverlayWindow overlay)
    {
        var hotkeys = _host.Services.GetRequiredService<IGlobalHotkeyService>() as GlobalHotkeyService;
        if (hotkeys is null) return;

        var hwnd = new WindowInteropHelper(overlay).Handle;
        hotkeys.Initialize(hwnd);

        hotkeys.Register(HotkeyId.ToggleSession,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Space);
        hotkeys.Register(HotkeyId.CaptureScreen,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.S);
        hotkeys.Register(HotkeyId.GenerateAnswer, ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q);
        hotkeys.Register(HotkeyId.CopyAnswer,     ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.C);
        hotkeys.Register(HotkeyId.ToggleOverlay,  ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.H);

        var sessionVm = _host.Services.GetRequiredService<SessionControlViewModel>();
        var turnVm2   = _host.Services.GetRequiredService<ConversationTurnViewModel>();

        hotkeys.HotkeyPressed += (_, id) =>
        {
            switch (id)
            {
                case HotkeyId.ToggleSession:
                    _ = sessionVm.ToggleSessionCommand.ExecuteAsync(null);
                    break;
                case HotkeyId.CaptureScreen:
                    _ = turnVm2.CaptureScreenCommand.ExecuteAsync(null);
                    break;
                case HotkeyId.GenerateAnswer:
                    _ = turnVm2.RegenerateCommand.ExecuteAsync(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.CopyAnswer:
                    turnVm2.CopyLatestCommand.Execute(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.ToggleOverlay:
                    sessionVm.ToggleSidebarCommand.Execute(null);
                    break;
            }
        };
    }

    /// <inheritdoc/>
    protected override async void OnExit(ExitEventArgs e)
    {
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}
