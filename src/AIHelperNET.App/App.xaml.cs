using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.Streaming;
using AIHelperNET.App.ViewModels;
using AIHelperNET.App.Windows;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Common;
using AIHelperNET.Infrastructure.Hotkeys;
using AIHelperNET.Infrastructure.Ocr;
using AIHelperNET.Infrastructure.Persistence;
using AIHelperNET.Infrastructure.Transcription;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace AIHelperNET.App;

/// <summary>Application entry point and host lifecycle coordinator.</summary>
public partial class App : System.Windows.Application
{
    private IHost _host = null!;

    /// <inheritdoc/>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => Log.Fatal(ex.Exception, "Unhandled dispatcher exception");

        var builder = Host.CreateApplicationBuilder();
        builder.ConfigureAIHelper();
        _host = builder.Build();

        using (var scope = _host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            try
            {
                await db.Database.MigrateAsync();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Database migration failed at startup");
                MessageBox.Show(
                    $"The database could not be upgraded:\n\n{ex.Message}\n\n" +
                    "This usually means an older database file predates migrations. " +
                    "Back up and delete it (plus its -wal/-shm siblings), then restart:\n\n" +
                    AppPaths.DatabaseFile,
                    "AIHelperNET — database upgrade failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(1);
                return;
            }
        }

        await _host.StartAsync();

        var overlay = _host.Services.GetRequiredService<MainOverlayWindow>();

        // Resolve ConversationTurnViewModel early — referenced in both transcript and answer sink handlers.
        var turnVm = _host.Services.GetRequiredService<ConversationTurnViewModel>();

        // Wire TranscriptSink → TranscriptViewModel
        var transcriptSink = _host.Services.GetRequiredService<TranscriptSink>();
        var transcriptVm   = _host.Services.GetRequiredService<TranscriptViewModel>();
        transcriptSink.SetHandler(item =>
        {
            transcriptVm.AddItem(item);

            // Keep the last 5 interviewer lines available for screen-analysis prompts.
            if (item.Speaker == AIHelperNET.Domain.Sessions.Speaker.Other)
            {
                var last5 = transcriptVm.Items
                    .Where(i => i.SpeakerLabel == "Other")
                    .TakeLast(5)
                    .Select(i => i.Text);
                turnVm.UpdateInterviewerLines(last5);
            }
        });

        // Wire AnswerStreamSink → ConversationTurnViewModel
        var answerSink = _host.Services.GetRequiredService<AnswerStreamSink>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => turnVm.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));

        // Wire ConversationTurnSinkAdapter → ConversationTurnViewModel
        var turnCreatedSink = _host.Services.GetRequiredService<ConversationTurnSinkAdapter>();
        turnCreatedSink.SetHandler((id, question) => turnVm.AddTurn(id, question));
        turnCreatedSink.SetStatusHandler((id, status) =>
            turnVm.GetTurn(id)?.Status = status);

        try { await turnVm.LoadFontSizeAsync(); }
        catch (Exception ex) { Log.Warning(ex, "Failed to restore answer font size; using default"); }

        // Start always-on audio level monitoring
        var levelMonitor = _host.Services.GetRequiredService<IAudioLevelMonitor>();
        try
        {
            var settingsStore  = _host.Services.GetRequiredService<ISettingsStore>();
            var appSettings    = await settingsStore.LoadAsync(CancellationToken.None);
            await levelMonitor.StartAsync(appSettings.MicDeviceId, appSettings.LoopbackDeviceId, CancellationToken.None);
        }
        catch (Exception ex) { Log.Warning(ex, "Failed to start audio level monitor"); }

        var levelVm = _host.Services.GetRequiredService<AudioLevelViewModel>();
        levelVm.Subscribe();

        ScreenGrabber.StartTracking();
        overlay.Show();
        WireHotkeys(overlay);
        PreWarmWhisperModel();
        PreWarmSileroModel();
    }

    private void PreWarmWhisperModel()
    {
        var modelProvider  = _host.Services.GetRequiredService<WhisperModelProvider>();
        var settingsStore  = _host.Services.GetRequiredService<AIHelperNET.Application.Abstractions.ISettingsStore>();
        _ = Task.Run(async () =>
        {
            try
            {
                var settings = await settingsStore.LoadAsync(CancellationToken.None);
                Log.Information("Whisper: pre-warming {Model} model in background…", settings.WhisperModel);
                await modelProvider.GetFactoryAsync(settings.WhisperModel, CancellationToken.None);
                Log.Information("Whisper: model ready");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Whisper: pre-warm failed");
                return;
            }

            // Download Medium only after the active model is loaded so the semaphore
            // is free for session start while the large download runs.
            _ = Task.Run(async () =>
            {
                try
                {
                    Log.Information("Whisper: downloading Medium model in background…");
                    await modelProvider.GetFactoryAsync(WhisperModelSize.Medium, CancellationToken.None);
                    Log.Information("Whisper: Medium model ready");
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Whisper: Medium model download failed");
                }
            });
        });
    }

    private void PreWarmSileroModel()
    {
        var provider = _host.Services.GetRequiredService<SileroModelProvider>();
        _ = Task.Run(async () =>
        {
            try
            {
                Log.Information("Silero: pre-warming VAD model in background…");
                await provider.GetSessionAsync(CancellationToken.None);
                Log.Information("Silero: VAD model ready");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Silero: pre-warm failed");
            }
        });
    }

    private void WireHotkeys(MainOverlayWindow overlay)
    {
        var hotkeys = _host.Services.GetRequiredService<IGlobalHotkeyService>() as GlobalHotkeyService;
        if (hotkeys is null) return;

        var hwnd = new WindowInteropHelper(overlay).Handle;
        hotkeys.Initialize(hwnd);

        // Register from the single source of truth so the Settings shortcut list can never drift.
        foreach (var binding in HotkeyDefaults.All)
            hotkeys.Register(binding.Id, binding.Modifiers, binding.Key);

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
                    _ = turnVm2.CaptureScreenCommand.ExecuteAsync(sessionVm);
                    break;
                case HotkeyId.GenerateAnswer:
                    _ = turnVm2.RegenerateCommand.ExecuteAsync(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.CopyAnswer:
                    turnVm2.CopyLatestCommand.Execute(turnVm2.Turns.FirstOrDefault());
                    break;
                case HotkeyId.AnswerLatestQuestion:
                    _ = turnVm2.AnswerLatestQuestionCommand.ExecuteAsync(null);
                    break;
                case HotkeyId.ToggleOverlay:
                    if (overlay.IsVisible)
                        overlay.Hide();
                    else
                    {
                        overlay.Show();
                        overlay.Activate();
                        overlay.WindowState = WindowState.Normal;
                    }
                    break;
            }
        };
    }

    /// <inheritdoc/>
    protected override async void OnExit(ExitEventArgs e)
    {
        ScreenGrabber.StopTracking();
        _host.Services.GetService<IGlobalHotkeyService>()?.UnregisterAll();
        var monitor = _host.Services.GetService<IAudioLevelMonitor>();
        if (monitor is not null) await monitor.StopAsync();
        using (_host) await _host.StopAsync();
        Serilog.Log.CloseAndFlush();
        base.OnExit(e);
    }
}

static class ThemeManager
{
    const string DarkUri  = "Resources/DarkTheme.xaml";
    const string LightUri = "Resources/LightTheme.xaml";

    static bool _isDark = true;

    public static void Toggle()
    {
        var dicts   = System.Windows.Application.Current.Resources.MergedDictionaries;
        var current = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains("Theme") == true);
        if (current is null) return;
        dicts.Remove(current);
        _isDark = !_isDark;
        dicts.Insert(0, new ResourceDictionary
        {
            Source = new Uri(_isDark ? DarkUri : LightUri, UriKind.Relative)
        });
    }
}
