// src/AIHelperNET.App/Windows/ReviewWindow.xaml.cs
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;

namespace AIHelperNET.App.Windows;

/// <summary>Modeless window that displays the post-session review for a single session.</summary>
[SupportedOSPlatform("windows")]
public sealed partial class ReviewWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly SessionReviewViewModel _vm;
    private bool _stealthEnabled = true; // mirrors the overlay's default

    /// <summary>Initialises a new <see cref="ReviewWindow"/>.</summary>
    public ReviewWindow(SessionReviewViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
    }

    /// <summary>Mirrors the overlay's stealth state on this window.</summary>
    public void SetStealth(bool enable)
    {
        _stealthEnabled = enable;
        ApplyDisplayAffinity();
    }

    private void ApplyDisplayAffinity()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // not yet shown; OnSourceInitialized applies it
        SetWindowDisplayAffinity(hwnd, _stealthEnabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    /// <inheritdoc/>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDisplayAffinity();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await _vm.LoadAsync();

    /// <inheritdoc/>
    protected override void OnClosed(EventArgs e)
    {
        _vm.Cancel();
        _vm.Dispose();
        base.OnClosed(e);
    }
}
