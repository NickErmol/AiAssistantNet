using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.App.Streaming;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

public sealed partial class OverlayViewModel : ObservableObject
{
    private readonly IMediator _mediator;
    private readonly AnswerStreamSink _streamSink;

    [ObservableProperty] private string _currentAnswer = string.Empty;
    [ObservableProperty] private bool _isSessionActive;
    [ObservableProperty] private string _latestQuestion = string.Empty;
    [ObservableProperty] private bool _isVisible = true;

    private SessionId? _sessionId;
    private QuestionId? _latestQuestionId;
    private CancellationTokenSource? _answerCts;
    private string? _lastScreenContext;

    public OverlayViewModel(IMediator mediator, AnswerStreamSink streamSink)
    {
        _mediator = mediator;
        _streamSink = streamSink;
        _streamSink.SetHandler(OnChunk);
    }

    [RelayCommand]
    private async Task ToggleSessionAsync()
    {
        if (!IsSessionActive)
        {
            var settingsResult = await _mediator.Send(new GetSettingsQuery());
            var settings = settingsResult.IsSuccess ? settingsResult.Value : null;

            var result = await _mediator.Send(new StartSessionCommand(
                settings?.AnswerSettings ?? AnswerSettings.Default,
                settings?.CodeProfile   ?? CodeProfile.Empty));

            if (result.IsSuccess)
            {
                _sessionId = result.Value.Id;
                IsSessionActive = true;
                CurrentAnswer = string.Empty;
                LatestQuestion = string.Empty;
            }
        }
        else if (_sessionId is { } id)
        {
            _answerCts?.Cancel();
            await _mediator.Send(new StopSessionCommand(id));
            IsSessionActive = false;
            _sessionId = null;
        }
    }

    [RelayCommand]
    private async Task GenerateAnswerAsync()
    {
        if (_sessionId is not { } id || _latestQuestionId is not { } qid) return;

        _answerCts?.Cancel();
        _answerCts = new CancellationTokenSource();
        CurrentAnswer = string.Empty;

        await _mediator.Send(
            new GenerateAnswerCommand(id, qid, _lastScreenContext),
            _answerCts.Token);
    }

    [RelayCommand]
    private async Task CaptureScreenAsync()
    {
        var result = await _mediator.Send(new CaptureScreenCommand());
        if (result.IsSuccess) _lastScreenContext = result.Value;
    }

    [RelayCommand]
    private void CopyAnswer()
    {
        if (!string.IsNullOrEmpty(CurrentAnswer))
            System.Windows.Clipboard.SetText(CurrentAnswer);
    }

    [RelayCommand]
    private void ToggleVisibility() => IsVisible = !IsVisible;

    public void OnQuestionDetected(string questionText, QuestionId questionId)
    {
        LatestQuestion = questionText;
        _latestQuestionId = questionId;
    }

    private void OnChunk(AnswerId answerId, string chunk)
    {
        CurrentAnswer += chunk;
    }
}
