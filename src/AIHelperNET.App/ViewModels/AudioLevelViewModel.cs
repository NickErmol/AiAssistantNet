using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.ViewModels;

/// <summary>Exposes real-time audio peak levels for mic and loopback to the UI.</summary>
public sealed partial class AudioLevelViewModel(IAudioLevelMonitor monitor) : ObservableObject
{
    /// <summary>Gets the current microphone peak level in [0.0, 1.0].</summary>
    [ObservableProperty] private double _micLevel;

    /// <summary>Gets the current system audio (loopback) peak level in [0.0, 1.0].</summary>
    [ObservableProperty] private double _systemLevel;

    /// <summary>Subscribes to monitor events and forwards updates to the UI thread.</summary>
    public void Subscribe()
    {
        monitor.MicLevelChanged    += level => System.Windows.Application.Current.Dispatcher
            .BeginInvoke(() => MicLevel    = level);
        monitor.SystemLevelChanged += level => System.Windows.Application.Current.Dispatcher
            .BeginInvoke(() => SystemLevel = level);
    }
}
