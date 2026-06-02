using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed partial class SettingsViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private AppSettingsDto? _settings;
    [ObservableProperty] private string _apiKeyInput = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    [RelayCommand]
    private async Task LoadAsync()
    {
        var result = await mediator.Send(new GetSettingsQuery());
        if (result.IsSuccess) Settings = result.Value;
    }

    [RelayCommand]
    private async Task SaveApiKeyAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKeyInput)) return;

        var secure = new System.Security.SecureString();
        foreach (var c in ApiKeyInput) secure.AppendChar(c);
        secure.MakeReadOnly();

        var result = await mediator.Send(new SaveApiKeyCommand(secure));
        StatusMessage = result.IsSuccess ? "API key saved." : $"Error: {string.Join(", ", result.Errors)}";
        ApiKeyInput = string.Empty;
    }
}
