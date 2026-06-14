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
        // Window.Opacity (not AllowsTransparency) drives the see-through: it keeps the window
        // DWM-composited so SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE) still works. Per-pixel
        // AllowsTransparency would make it a layered window and break stealth on this OS.
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

        // Keep the settings window in lockstep with the overlay's stealth state.
        _settingsWindow.SetStealth(_stealthActive);

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
        // Dock the settings window to the right of the overlay, top-aligned, so the two sit
        // side by side instead of overlapping (the overlay's z-order would otherwise hide it).
        // Positioning manually rather than via Owner avoids reparenting the window — Owner would
        // drop it from the desktop's top-level window list and tie its z-order/minimize to the
        // overlay. Falls back to the left side when there isn't room on the right.
        const double gap = 8;
        var rightEdge = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;

        var left = Left + ActualWidth + gap;
        if (left + _settingsWindow.Width > rightEdge)
            left = Left - _settingsWindow.Width - gap;

        _settingsWindow.Left = left;
        _settingsWindow.Top  = Top;
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
