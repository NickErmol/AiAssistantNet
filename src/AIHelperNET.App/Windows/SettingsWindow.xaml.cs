using System.Windows;
using AIHelperNET.App.ViewModels;

namespace AIHelperNET.App.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _vm.LoadCommand.ExecuteAsync(null);
    }

    private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        => _vm.ApiKeyInput = ApiKeyBox.Password;
}
