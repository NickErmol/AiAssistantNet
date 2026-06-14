// src/AIHelperNET.App/Windows/SettingsWindow.xaml.cs
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using AIHelperNET.App.Hotkeys;
using AIHelperNET.App.ViewModels;
using NAudio.CoreAudioApi;

namespace AIHelperNET.App.Windows;

[SupportedOSPlatform("windows")]
public sealed partial class SettingsWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private const uint WDA_NONE               = 0x00000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    private readonly SettingsViewModel _vm;
    private bool _stealthEnabled = true; // mirrors the overlay's default (stealth on)

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm         = vm;
        DataContext = vm;
    }

    // Mirror the overlay's stealth state: when stealth is on, exclude the settings window from
    // screen capture/sharing too (it holds sensitive config — API key field, device names);
    // when off, let it be captured like a normal window. Driven by the overlay's 👁/🎥 toggle.
    public void SetStealth(bool enable)
    {
        _stealthEnabled = enable;
        ApplyDisplayAffinity();
    }

    private void ApplyDisplayAffinity()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return; // not yet shown; OnSourceInitialized applies it later
        SetWindowDisplayAffinity(hwnd, _stealthEnabled ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDisplayAffinity();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        PopulateAudioDevices();
        await _vm.LoadAsync();
    }

    private void PopulateAudioDevices()
    {
        using var enumerator = new MMDeviceEnumerator();

        var mics = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
        MicCombo.ItemsSource = mics.Select(d => new AudioDeviceItem(d.ID, d.FriendlyName)).ToList();

        var loopbacks = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        LoopbackCombo.ItemsSource = loopbacks.Select(d => new AudioDeviceItem(d.ID, d.FriendlyName)).ToList();
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiKeyInput = ApiKeyBox.Password;

    // Closing a singleton DI window destroys it — subsequent Show() calls would throw.
    // Hide instead so the instance stays reusable.
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_vm.IsAnyRowRecording) return;

        if (e.Key == Key.Escape) { _vm.CancelRecording(); e.Handled = true; return; }

        var key = e.Key == Key.System ? e.SystemKey : e.Key; // Alt chords arrive as Key.System
        if (KeyGestureCapture.IsModifierKey(key)) { e.Handled = true; return; } // wait for the real key

        if (KeyGestureCapture.TryTranslate(key, Keyboard.Modifiers, out var mods, out var vk))
            _vm.ApplyRecordedChord(mods, vk);
        else
            _vm.SetRecordingError("Unsupported key — pick a letter, digit, F-key, or Space.");

        e.Handled = true;
    }
}

public sealed record AudioDeviceItem(string Id, string FriendlyName);
