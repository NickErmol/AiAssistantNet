using System.Windows;
using System.Windows.Interop;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AIHelperNET.App;

public partial class App : System.Windows.Application
{
    private IHost _host = null!;

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

        var overlay = _host.Services.GetRequiredService<OverlayWindow>();
        overlay.Show();

        WireHotkeys(overlay);
    }

    private void WireHotkeys(OverlayWindow overlay)
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

        var vm = _host.Services.GetRequiredService<OverlayViewModel>();
        hotkeys.HotkeyPressed += (_, id) =>
        {
            switch (id)
            {
                case HotkeyId.ToggleSession:  _ = vm.ToggleSessionCommand.ExecuteAsync(null); break;
                case HotkeyId.CaptureScreen:  _ = vm.CaptureScreenCommand.ExecuteAsync(null); break;
                case HotkeyId.GenerateAnswer: _ = vm.GenerateAnswerCommand.ExecuteAsync(null); break;
                case HotkeyId.CopyAnswer:     vm.CopyAnswerCommand.Execute(null); break;
                case HotkeyId.ToggleOverlay:  vm.ToggleVisibilityCommand.Execute(null); break;
            }
        };
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}
