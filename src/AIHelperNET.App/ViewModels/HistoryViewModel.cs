using System.Collections.ObjectModel;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Ids;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;
using Microsoft.Win32;

namespace AIHelperNET.App.ViewModels;

public sealed class SessionSummaryVm(SessionSummaryDto dto) : ObservableObject
{
    public SessionId         Id            => dto.Id;
    public string            DateLabel     => dto.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public string            Mode          => dto.State.ToString();
    public int               QuestionCount => dto.QuestionCount;
    public int               AnswerCount   => dto.AnswerCount;
    public bool              IsActive      => dto.EndedAt is null;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    private SessionDetailDto? _detail;
    public SessionDetailDto? Detail
    {
        get => _detail;
        set => SetProperty(ref _detail, value);
    }
}

public sealed partial class HistoryViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public ObservableCollection<SessionSummaryVm> Sessions { get; } = [];

    [RelayCommand]
    public async Task LoadAsync()
    {
        var result = await mediator.Send(new GetSessionHistoryQuery(50));
        if (!result.IsSuccess) return;
        Sessions.Clear();
        foreach (var dto in result.Value)
            Sessions.Add(new SessionSummaryVm(dto));
    }

    [RelayCommand]
    public async Task ToggleExpandAsync(SessionSummaryVm? vm)
    {
        if (vm is null) return;
        if (!vm.IsExpanded)
        {
            if (vm.Detail is null)
            {
                var result = await mediator.Send(new GetSessionDetailQuery(vm.Id));
                if (result.IsSuccess) vm.Detail = result.Value;
            }
            vm.IsExpanded = true;
        }
        else
        {
            vm.IsExpanded = false;
        }
    }

    [RelayCommand]
    public async Task ExportAsync(SessionSummaryVm? vm)
    {
        if (vm is null) return;

        var dlg = new SaveFileDialog
        {
            Title      = "Export session",
            Filter     = "Markdown (*.md)|*.md|Text (*.txt)|*.txt",
            FileName   = $"session-{vm.DateLabel.Replace(":", "-")}",
            DefaultExt = ".md"
        };

        if (dlg.ShowDialog() != true) return;

        var format = dlg.FilterIndex == 2 ? ExportFormat.Txt : ExportFormat.Markdown;
        var result = await mediator.Send(new ExportSessionCommand(vm.Id, format, dlg.FileName));
        StatusMessage = result.IsSuccess ? "Exported ✓" : $"Error: {string.Join(", ", result.Errors)}";
    }
}
