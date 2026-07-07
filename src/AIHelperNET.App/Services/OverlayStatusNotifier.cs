using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.Services;

/// <summary>Holds the overlay header's transient status line; safe to call from any thread.</summary>
public sealed class OverlayStatusNotifier : ObservableObject, IOverlayStatusNotifier
{
    private string _message = string.Empty;

    /// <summary>Current transient message; empty when nothing to show.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public void Notify(string message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Message = message;
        else dispatcher.BeginInvoke(() => Message = message);
    }
}
