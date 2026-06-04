using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;
using Serilog;

namespace AIHelperNET.App.Windows;

/// <summary>Composite data context for <see cref="MainOverlayWindow"/>.</summary>
public sealed class MainOverlayWindowContext(
    SessionControlViewModel sessionControl,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn,
    AudioLevelViewModel audioLevel)
{
    /// <summary>Gets the session control view model.</summary>
    public SessionControlViewModel SessionControl    => sessionControl;

    /// <summary>Gets the transcript view model.</summary>
    public TranscriptViewModel Transcript            => transcript;

    /// <summary>Gets the conversation turn view model.</summary>
    public ConversationTurnViewModel ConversationTurn => conversationTurn;

    /// <summary>Gets the audio level view model.</summary>
    public AudioLevelViewModel AudioLevel             => audioLevel;
}

/// <summary>The stealth overlay window excluded from screen capture.</summary>
[SupportedOSPlatform("windows")]
public partial class MainOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_NONE             = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly SettingsWindow    _settingsWindow;
    private readonly SettingsViewModel _settingsVm;
    private readonly HistoryViewModel  _historyVm;
    private bool _stealthActive;
    private bool _showingHistory;

    /// <summary>Initialises a new instance of <see cref="MainOverlayWindow"/>.</summary>
    public MainOverlayWindow(
        MainOverlayWindowContext context,
        SettingsWindow settingsWindow,
        SettingsViewModel settingsVm,
        HistoryViewModel historyVm)
    {
        InitializeComponent();
        DataContext     = context;
        _settingsWindow = settingsWindow;
        _settingsVm     = settingsVm;
        _historyVm      = historyVm;
        _settingsVm.OpacityChanged += opacity => Opacity = opacity;
        HistoryPanelControl.DataContext = _historyVm;
    }

    /// <inheritdoc/>
    protected override async void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyStealth(enable: true); // stealth on by default; toggle with 🎥 button

        // Load persisted opacity before the window becomes visible
        try
        {
            await _settingsVm.LoadAsync();
            Opacity = _settingsVm.OverlayOpacity;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to restore overlay opacity; using default");
        }
    }

    private void ApplyStealth(bool enable)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _stealthActive = enable && SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        if (!_stealthActive) SetWindowDisplayAffinity(hwnd, WDA_NONE);

        if (StealthBtn is not null)
            StealthBtn.Content = _stealthActive ? "👁" : "🎥";

        Log.Information("Overlay: stealth={S}", _stealthActive);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void ToggleStealth_Click(object sender, RoutedEventArgs e)
        => ApplyStealth(!_stealthActive);

    private void Minimize_Click(object sender, RoutedEventArgs e)
        => Hide();

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ToggleTheme_Click(object sender, RoutedEventArgs e)
        => ThemeManager.Toggle();

    private async void ToggleHistory_Click(object sender, RoutedEventArgs e)
    {
        _showingHistory = !_showingHistory;
        LiveView.Visibility            = _showingHistory ? Visibility.Collapsed : Visibility.Visible;
        HistoryPanelControl.Visibility = _showingHistory ? Visibility.Visible   : Visibility.Collapsed;
        if (_showingHistory)
            await _historyVm.LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
