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
    ConversationTurnViewModel conversationTurn)
{
    /// <summary>Gets the session control view model.</summary>
    public SessionControlViewModel SessionControl    => sessionControl;

    /// <summary>Gets the transcript view model.</summary>
    public TranscriptViewModel Transcript            => transcript;

    /// <summary>Gets the conversation turn view model.</summary>
    public ConversationTurnViewModel ConversationTurn => conversationTurn;
}

/// <summary>The stealth overlay window excluded from screen capture.</summary>
[SupportedOSPlatform("windows")]
public partial class MainOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_NONE             = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly SettingsWindow _settingsWindow;
    private bool _stealthActive;

    /// <summary>Initialises a new instance of <see cref="MainOverlayWindow"/>.</summary>
    public MainOverlayWindow(MainOverlayWindowContext context, SettingsWindow settingsWindow)
    {
        InitializeComponent();
        DataContext     = context;
        _settingsWindow = settingsWindow;
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyStealth(enable: true); // stealth on by default; toggle with 🎥 button
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

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();
}
