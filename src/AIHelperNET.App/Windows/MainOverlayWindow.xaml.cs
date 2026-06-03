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

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_MONITOR            = 0x00000001;

    private readonly SettingsWindow _settingsWindow;

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
        var hwnd = new WindowInteropHelper(this).Handle;

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            if (!SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                Log.Warning("SetWindowDisplayAffinity failed — overlay may be visible to screen capture");
            else
                Log.Information("Overlay: WDA_MONITOR applied");
        }
        else
        {
            Log.Information("Overlay: WDA_EXCLUDEFROMCAPTURE applied");
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => DragMove();

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
