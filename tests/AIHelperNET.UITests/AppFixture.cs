using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace AIHelperNET.UITests;

public sealed class AppFixture : IAsyncLifetime
{
    private static readonly string ExePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\..\src\AIHelperNET.App\bin\Debug\net10.0-windows10.0.17763.0\AIHelperNET.App.exe"));

    private Application? _app;

    public UIA3Automation Automation { get; } = new();
    public Application App => _app!;
    public Window Window { get; private set; } = null!;
    public MainWindow Main { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        foreach (var p in Process.GetProcessesByName("AIHelperNET.App"))
        {
            p.Kill();
            await p.WaitForExitAsync();
        }

        await Task.Delay(800);

        _app = Application.Launch(ExePath);
        Window = _app.GetMainWindow(Automation, TimeSpan.FromSeconds(15));

        // Allow app to finish initializing (VAD + Whisper warm-up)
        await Task.Delay(3000);

        Main = new MainWindow(Window);
    }

    public Task DisposeAsync()
    {
        _app?.Kill();
        Automation.Dispose();
        return Task.CompletedTask;
    }
}
