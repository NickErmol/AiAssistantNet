using System.Collections.ObjectModel;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Mediator;

namespace AIHelperNET.App.ViewModels;

/// <summary>Displays a single answer version within a conversation turn.</summary>
public sealed class AnswerVersionVm(AnswerVersionId id, AnswerVersionType type, DateTimeOffset createdAt)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>Gets the answer version identifier.</summary>
    public AnswerVersionId Id           => id;

    /// <summary>Gets the human-readable label for this version type.</summary>
    public string VersionLabel          => type switch
    {
        AnswerVersionType.Preliminary               => "Preliminary",
        AnswerVersionType.RefinedAfterClarification => "Refined — after clarification",
        AnswerVersionType.UpdatedWithScreen         => "Screen analysis",
        AnswerVersionType.ManuallyRegenerated       => "Manually regenerated",
        AnswerVersionType.FollowUp                  => "Follow-up",
        _                                           => type.ToString()
    };

    /// <summary>Gets the formatted creation time label.</summary>
    public string TimeLabel             => createdAt.ToLocalTime().ToString("HH:mm:ss");

    private string _text = string.Empty;
    /// <summary>Gets or sets the accumulated answer text.</summary>
    public string Text
    {
        get => _text;
        set => SetProperty(ref _text, value);
    }

    private bool _isLatest;
    /// <summary>Gets or sets a value indicating whether this is the latest version.</summary>
    public bool IsLatest
    {
        get => _isLatest;
        set => SetProperty(ref _isLatest, value);
    }
}

/// <summary>Represents a single conversation turn (question + answers) in the UI.</summary>
public sealed class TurnVm(ConversationTurnId id, string initialQuestion)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>Gets the turn identifier.</summary>
    public ConversationTurnId Id                              => id;

    /// <summary>Gets the text of the initial question.</summary>
    public string InitialQuestion                             => initialQuestion;

    private ConversationTurnStatus _status = ConversationTurnStatus.Detected;
    /// <summary>Gets or sets the current turn status.</summary>
    public ConversationTurnStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
                OnPropertyChanged(nameof(StatusLabel));
        }
    }

    /// <summary>Gets the human-readable status label for display.</summary>
    public string StatusLabel => Status switch
    {
        ConversationTurnStatus.CollectingQuestion     => "Collecting question...",
        ConversationTurnStatus.Detected               => "Detected",
        ConversationTurnStatus.GeneratingPreliminary  => "Generating...",
        ConversationTurnStatus.PreliminaryReady       => "Ready",
        ConversationTurnStatus.UpdatedContextReceived => "Updating context...",
        ConversationTurnStatus.AwaitingClarification  => "Awaiting clarification...",
        ConversationTurnStatus.ClarificationReceived  => "Clarification received",
        ConversationTurnStatus.GeneratingRefined      => "Refining...",
        ConversationTurnStatus.RefinedReady           => "Refined",
        ConversationTurnStatus.Dismissed              => "Dismissed",
        ConversationTurnStatus.Resolved               => "Resolved",
        _                                             => Status.ToString()
    };

    /// <summary>Gets all answer versions for this turn.</summary>
    public ObservableCollection<AnswerVersionVm> AnswerVersions { get; } = [];

    private AnswerVersionVm? _latestVersion;
    /// <summary>Gets or sets the latest answer version (observable — drives the answer text binding).</summary>
    public AnswerVersionVm? LatestVersion
    {
        get => _latestVersion;
        set => SetProperty(ref _latestVersion, value);
    }

    private string _followUpText = string.Empty;
    /// <summary>Gets or sets the follow-up question text typed by the user for this turn.</summary>
    public string FollowUpText
    {
        get => _followUpText;
        set => SetProperty(ref _followUpText, value);
    }
}

/// <summary>Manages the list of conversation turns and routes streaming events to the correct turn.</summary>
public sealed partial class ConversationTurnViewModel(IMediator mediator) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TurnVm> _turns = [];
    [ObservableProperty] private SessionId? _activeSessionId;
    [ObservableProperty] private int _answerFontSize = 12;
    [ObservableProperty] private bool _isFollowUpEnabled;

    private bool CanIncrease() => AnswerFontSize < SaveAnswerFontSizeHandler.Max;
    private bool CanDecrease() => AnswerFontSize > SaveAnswerFontSizeHandler.Min;

    [RelayCommand(CanExecute = nameof(CanIncrease))]
    private async Task IncreaseFontSizeAsync()
    {
        AnswerFontSize++;
        IncreaseFontSizeCommand.NotifyCanExecuteChanged();
        DecreaseFontSizeCommand.NotifyCanExecuteChanged();
        await mediator.Send(new SaveAnswerFontSizeCommand(AnswerFontSize));
    }

    [RelayCommand(CanExecute = nameof(CanDecrease))]
    private async Task DecreaseFontSizeAsync()
    {
        AnswerFontSize--;
        IncreaseFontSizeCommand.NotifyCanExecuteChanged();
        DecreaseFontSizeCommand.NotifyCanExecuteChanged();
        await mediator.Send(new SaveAnswerFontSizeCommand(AnswerFontSize));
    }

    /// <summary>Restores the persisted answer font size from settings. Call once at startup.</summary>
    public async Task LoadFontSizeAsync()
    {
        var result = await mediator.Send(new GetSettingsQuery());
        if (result.IsSuccess)
            AnswerFontSize = result.Value.AnswerFontSize;
    }

    /// <summary>Looks up a turn by its identifier.</summary>
    public TurnVm? GetTurn(ConversationTurnId id)
        => Turns.FirstOrDefault(t => t.Id == id);

    /// <summary>Appends a streaming chunk to the active version of the given turn.</summary>
    public void OnChunk(ConversationTurnId turnId, AnswerVersionType versionType, string chunk)
    {
        var turn = Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn is null) return;

        var version = turn.AnswerVersions.FirstOrDefault(v => v.IsLatest)
            ?? CreateNewVersion(turn, AnswerVersionId.New(), versionType);
        version.Text += chunk;
    }

    /// <summary>Called when streaming for the given turn and version type completes.</summary>
    public static void OnComplete(ConversationTurnId turnId, AnswerVersionType versionType)
    {
        // Version already marked IsLatest during streaming — no further action needed.
    }

    /// <summary>Appends an error version to the given turn.</summary>
    public void OnError(ConversationTurnId turnId, string errorMessage)
    {
        var turn = Turns.FirstOrDefault(t => t.Id == turnId);
        if (turn is null) return;
        CreateNewVersion(turn, AnswerVersionId.New(), AnswerVersionType.Preliminary,
            $"[Error: {errorMessage}]");
    }

    private static AnswerVersionVm CreateNewVersion(
        TurnVm turn, AnswerVersionId id, AnswerVersionType type, string text = "")
    {
        foreach (var v in turn.AnswerVersions) v.IsLatest = false;
        var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
            { Text = text, IsLatest = true };
        turn.AnswerVersions.Insert(0, version);
        turn.LatestVersion = version;
        return version;
    }

    /// <summary>Adds a new turn to the top of the list.</summary>
    public void AddTurn(ConversationTurnId id, string question)
        => Turns.Insert(0, new TurnVm(id, question));

    /// <summary>Clears all turns and resets the active session.</summary>
    public void Clear() { Turns.Clear(); ActiveSessionId = null; }

    /// <summary>Regenerates the answer for the given turn, optionally with screen context.</summary>
    [RelayCommand]
    private async Task RegenerateAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new RegenerateAnswerCommand(sid, turn.Id));
    }

    /// <summary>Dismisses the given turn and removes it from the list.</summary>
    [RelayCommand]
    private async Task DismissAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new DismissTurnCommand(sid, turn.Id));
        Turns.Remove(turn);
    }

    /// <summary>Resolves the given turn and removes it from the list.</summary>
    [RelayCommand]
    private async Task ResolveAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        await mediator.Send(new ResolveTurnCommand(sid, turn.Id));
        Turns.Remove(turn);
    }

    /// <summary>Submits the follow-up question typed in a turn card and triggers answer generation.</summary>
    [RelayCommand]
    private async Task SubmitFollowUpAsync(TurnVm? turn)
    {
        if (turn is null || ActiveSessionId is not { } sid) return;
        if (string.IsNullOrWhiteSpace(turn.FollowUpText)) return;
        var text = turn.FollowUpText;
        turn.FollowUpText = string.Empty;
        await mediator.Send(new GenerateFollowUpCommand(sid, turn.Id, text));
    }

    /// <summary>Copies the latest answer version text to the clipboard.</summary>
    [RelayCommand]
    private static void CopyLatest(TurnVm? turn)
    {
        var text = turn?.LatestVersion?.Text;
        if (!string.IsNullOrEmpty(text))
            System.Windows.Clipboard.SetText(text);
    }

    /// <summary>Captures the current screen and either creates a new turn (if none exist) or updates the most recent one.</summary>
    [RelayCommand]
    private async Task CaptureScreenAsync(SessionControlViewModel? sessionControl)
    {
        if (ActiveSessionId is not { } sid) return;
        if (sessionControl is null) return;

        var ocrResult = await mediator.Send(new CaptureScreenCommand());
        if (ocrResult.IsFailed) return;

        string[] interviewerLines = sessionControl.IncludeInterviewerContext
            ? _lastInterviewerLines
            : [];

        var activeTurn = Turns.FirstOrDefault();
        if (activeTurn is null)
        {
            await mediator.Send(new StartScreenTurnCommand(
                sid, ocrResult.Value, sessionControl.ScreenAnalysisMode, interviewerLines));
            return;
        }

        await mediator.Send(new RegenerateAnswerWithScreenCommand(
            sid, activeTurn.Id, ocrResult.Value,
            sessionControl.ScreenAnalysisMode, interviewerLines));
    }

    private string[] _lastInterviewerLines = [];

    /// <summary>Updates the cached interviewer lines used as context for screen-based answer generation.</summary>
    /// <param name="lines">The most recent interviewer transcript lines.</param>
    public void UpdateInterviewerLines(IEnumerable<string> lines)
        => _lastInterviewerLines = lines.ToArray();
}
