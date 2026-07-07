using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using AIHelperNET.App.Services;
using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AIHelperNET.App.Windows;

/// <summary>Composite data context for <see cref="MainOverlayWindow"/>.</summary>
public sealed class MainOverlayWindowContext(
    SessionControlViewModel sessionControl,
    TranscriptViewModel transcript,
    ConversationTurnViewModel conversationTurn,
    AudioLevelViewModel audioLevel,
    OverlayStatusNotifier statusNotice)
{
    /// <summary>Gets the session control view model.</summary>
    public SessionControlViewModel SessionControl    => sessionControl;

    /// <summary>Gets the transcript view model.</summary>
    public TranscriptViewModel Transcript            => transcript;

    /// <summary>Gets the conversation turn view model.</summary>
    public ConversationTurnViewModel ConversationTurn => conversationTurn;

    /// <summary>Gets the audio level view model.</summary>
    public AudioLevelViewModel AudioLevel             => audioLevel;

    /// <summary>Gets the transient overlay status notifier.</summary>
    public OverlayStatusNotifier StatusNotice         => statusNotice;
}

/// <summary>The stealth overlay window excluded from screen capture.</summary>
[SupportedOSPlatform("windows")]
public partial class MainOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_NONE             = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // Surface brush keys overridden (window-local) with translucent versions in See-through mode,
    // so the whole overlay reads as glass while text/icons (Brush.Foreground.*) stay opaque.
    private static readonly string[] GlassSurfaceKeys =
    {
        "Brush.Background.Window", "Brush.Background.TitleBar",
        "Brush.Background.Sidebar", "Brush.Background.Panel", "Brush.Background.Card",
    };

    private readonly SettingsWindow              _settingsWindow;
    private readonly SettingsViewModel           _settingsVm;
    private readonly HistoryViewModel            _historyVm;
    private readonly IServiceScopeFactory        _reviewScopeFactory;
    private readonly List<ReviewWindow>          _reviewWindows = [];
    private bool _stealthActive;
    private bool _seeThrough;
    private bool _showingHistory;

    /// <summary>Initialises a new instance of <see cref="MainOverlayWindow"/>.</summary>
    public MainOverlayWindow(
        MainOverlayWindowContext context,
        SettingsWindow settingsWindow,
        SettingsViewModel settingsVm,
        HistoryViewModel historyVm,
        IServiceScopeFactory reviewScopeFactory)
    {
        InitializeComponent();
        DataContext          = context;
        _settingsWindow      = settingsWindow;
        _settingsVm          = settingsVm;
        _historyVm           = historyVm;
        _reviewScopeFactory  = reviewScopeFactory;
        _settingsVm.OpacityChanged += OnOverlayOpacityChanged;
        HistoryPanelControl.DataContext = _historyVm;
        _historyVm.ReviewRequested += OnReviewRequested;
    }

    /// <summary>
    /// Applies the persisted <see cref="OverlayMode"/>. MUST be called before the window is shown —
    /// <see cref="Window.AllowsTransparency"/> cannot change once the HWND exists. See-through turns on
    /// real per-pixel transparency (and forfeits stealth); Stealth keeps the window opaque and dims via
    /// <see cref="UIElement.Opacity"/> (stealth-safe).
    /// </summary>
    public void ApplyDisplayMode(OverlayMode mode)
    {
        _seeThrough = mode == OverlayMode.SeeThrough;
        if (_seeThrough)
        {
            AllowsTransparency = true; // legal only before the window is shown
            Opacity = 1.0;             // keep text crisp; see-through comes from translucent surfaces
            ApplySeeThroughGlass(_settingsVm.OverlayOpacity);
        }
        else
        {
            Opacity = _settingsVm.OverlayOpacity; // whole-window dim; stealth-safe
        }
    }

    // Drives the Transparency slider live: translucent surfaces in See-through mode, whole-window
    // dim in Stealth mode.
    private void OnOverlayOpacityChanged(double opacity)
    {
        if (_seeThrough) ApplySeeThroughGlass(opacity);
        else Opacity = opacity;
    }

    // Overrides the surface brushes with translucent copies of the current theme colors, window-local
    // so the Settings window keeps its opaque brushes. Rebuilt on slider change and after a theme toggle.
    private void ApplySeeThroughGlass(double opacity)
    {
        foreach (var key in GlassSurfaceKeys)
        {
            if (System.Windows.Application.Current?.TryFindResource(key) is SolidColorBrush baseBrush)
            {
                var brush = new SolidColorBrush(OverlayGlass.WithAlpha(baseBrush.Color, opacity));
                brush.Freeze();
                Resources[key] = brush;
            }
        }
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        if (_seeThrough)
        {
            // See-through is a layered window and cannot be stealthed — make that explicit and
            // disable the toggle so it can't be turned on.
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowDisplayAffinity(hwnd, WDA_NONE);
            _settingsWindow.SetStealth(false);
            if (StealthBtn is not null)
            {
                StealthBtn.IsEnabled = false;
                StealthBtn.ToolTip   = "Stealth is unavailable in See-through mode";
            }
            Log.Information("Overlay: see-through mode (stealth unavailable)");
        }
        else
        {
            ApplyStealth(enable: true); // stealth on by default; toggle with 🎥 button
        }
    }

    private void ApplyStealth(bool enable)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _stealthActive = enable && SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE);
        if (!_stealthActive) SetWindowDisplayAffinity(hwnd, WDA_NONE);

        // Keep the settings window and all open review windows in lockstep.
        _settingsWindow.SetStealth(_stealthActive);
        foreach (var rw in _reviewWindows)
            rw.SetStealth(_stealthActive);

        if (StealthBtn is not null)
            StealthBtn.Content = _stealthActive ? "👁" : "🎥";

        Log.Information("Overlay: stealth={S}", _stealthActive);
    }

    private void OnReviewRequested(AIHelperNET.Domain.Ids.SessionId sessionId)
    {
        var scope = _reviewScopeFactory.CreateScope();
        var vm = scope.ServiceProvider.GetRequiredService<SessionReviewViewModel>();
        vm.Initialize(sessionId);
        var window = new ReviewWindow(vm);
        _reviewWindows.Add(window);
        window.SetStealth(_stealthActive);
        window.Closed += (_, _) =>
        {
            _reviewWindows.Remove(window);
            scope.Dispose();
        };
        window.Show();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

    private void ToggleStealth_Click(object sender, RoutedEventArgs e)
    {
        if (_seeThrough) return; // stealth unavailable in see-through mode
        ApplyStealth(!_stealthActive);
    }

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
    {
        ThemeManager.Toggle();
        if (_seeThrough) ApplySeeThroughGlass(_settingsVm.OverlayOpacity); // rebuild from new theme colors
    }

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

    // The settings window isn't Owner-tied to the overlay (it lives independently in the desktop's
    // top-level window list), and it hides instead of closing — so it would linger and keep the app
    // alive after the overlay closes. Force it shut when the overlay closes.
    // Review windows are also non-owner, so close them all too.
    protected override void OnClosed(EventArgs e)
    {
        _historyVm.ReviewRequested -= OnReviewRequested;
        _settingsWindow.ForceClose();
        foreach (var rw in _reviewWindows.ToList())
            rw.Close();
        base.OnClosed(e);
    }
}
