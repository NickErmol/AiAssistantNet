using System.Diagnostics;
using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;
using Xunit;

namespace AIHelperNET.UITests;

public sealed class AppFixture : IAsyncLifetime
{
    // The App output exe is named TextInputHost.exe (see AIHelperNET.App.csproj). A real
    // Windows process of the same name also exists, so we always match on full path below.
    private static readonly string ExePath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\..\src\AIHelperNET.App\bin\Debug\net10.0-windows10.0.17763.0\TextInputHost.exe"));

    private Application? _app;

    public UIA3Automation Automation { get; } = new();
    public Application App => _app!;
    public Window Window { get; private set; } = null!;
    public MainWindow Main { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        foreach (var p in Process.GetProcessesByName("TextInputHost"))
        {
            // Only kill OUR build's exe — never the genuine Windows TextInputHost.exe.
            string? path;
            try { path = p.MainModule?.FileName; }
            catch { continue; } // access denied on a protected process => not ours
            if (!string.Equals(path, ExePath, StringComparison.OrdinalIgnoreCase)) continue;

            p.Kill(entireProcessTree: true);
            await Task.WhenAny(p.WaitForExitAsync(), Task.Delay(5000));
        }

        await Task.Delay(800);

        _app = Application.Launch(ExePath);

        // GetMainWindow relies on Process.MainWindowHandle which is never set for
        // ShowInTaskbar=False overlay windows. Poll GetAllTopLevelWindows instead.
        var deadline = DateTime.UtcNow.AddSeconds(15);
        Window? found = null;
        while (DateTime.UtcNow < deadline)
        {
            found = _app.GetAllTopLevelWindows(Automation)
                .FirstOrDefault(w =>
                    (w.Properties.Name.ValueOrDefault ?? string.Empty)
                    .Contains("AIHelper", StringComparison.OrdinalIgnoreCase));
            if (found != null) break;
            await Task.Delay(200);
        }

        Window = found ?? throw new InvalidOperationException(
            $"AIHelper window not found within 15 s. Exe: {ExePath}");

        // Allow app to finish initializing (VAD + Whisper warm-up)
        await Task.Delay(3000);

        Main = new MainWindow(Window);
    }

    public Task DisposeAsync()
    {
        try { _app?.Kill(); } catch { /* process may have already exited */ }
        Automation.Dispose();
        return Task.CompletedTask;
    }
}
