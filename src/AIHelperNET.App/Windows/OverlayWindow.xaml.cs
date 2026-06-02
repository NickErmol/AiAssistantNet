using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using AIHelperNET.App.ViewModels;
using Serilog;

namespace AIHelperNET.App.Windows;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;
    private const uint WDA_MONITOR            = 0x00000001;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

        if (!SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE))
        {
            if (!SetWindowDisplayAffinity(hwnd, WDA_MONITOR))
                Log.Warning("SetWindowDisplayAffinity failed — overlay may be visible to screen capture");
            else
                Log.Information("Overlay: WDA_MONITOR applied (WDA_EXCLUDEFROMCAPTURE not supported on this build)");
        }
        else
        {
            Log.Information("Overlay: WDA_EXCLUDEFROMCAPTURE applied");
        }
    }

    private void DragHandle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        => DragMove();
}
