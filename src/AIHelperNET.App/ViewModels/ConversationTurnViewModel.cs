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

    private bool _isError;
    /// <summary>Gets or sets a value indicating whether this version holds an error message
    /// rather than a normal answer (drives distinct error styling).</summary>
    public bool IsError
    {
        get => _isError;
        set => SetProperty(ref _isError, value);
    }
}

/// <summary>Coarse status grouping that drives the answer card's status color.</summary>
public enum TurnStatusKind
{
    /// <summary>An AI call is in flight (generating or refining).</summary>
    Busy,
    /// <summary>Waiting on the candidate's clarification before refining.</summary>
    Awaiting,
    /// <summary>An answer is available (preliminary, refined, or clarification received).</summary>
    Ready,
    /// <summary>The turn is finished (dismissed or resolved).</summary>
    Terminal,
    /// <summary>Detected but not yet generating or answered.</summary>
    Neutral
}

/// <summary>Represents a single conversation turn (question + answers) in the UI.</summary>
public sealed class TurnVm(ConversationTurnId id, string initialQuestion)
    : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    /// <summary>Gets the turn identifier.</summary>
    public ConversationTurnId Id                              => id;

    private string _initialQuestion = initialQuestion;
    /// <summary>Gets or sets the text of the initial question (updated to show the screen-capture count).</summary>
    public string InitialQuestion
    {
        get => _initialQuestion;
        set => SetProperty(ref _initialQuestion, value);
    }

    private ConversationTurnStatus _status = ConversationTurnStatus.Detected;
    /// <summary>Gets or sets the current turn status.</summary>
    public ConversationTurnStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusLabel));
                OnPropertyChanged(nameof(StatusKind));
                OnPropertyChanged(nameof(IsBusy));
            }
        }
    }

    /// <summary>Gets the coarse status grouping used to color the status line.</summary>
    public TurnStatusKind StatusKind => Status switch
    {
        ConversationTurnStatus.CollectingQuestion     or
        ConversationTurnStatus.GeneratingPreliminary  or
        ConversationTurnStatus.UpdatedContextReceived or
        ConversationTurnStatus.GeneratingRefined      => TurnStatusKind.Busy,

        ConversationTurnStatus.AwaitingClarification  => TurnStatusKind.Awaiting,

        ConversationTurnStatus.PreliminaryReady       or
        ConversationTurnStatus.ClarificationReceived  or
        ConversationTurnStatus.RefinedReady           => TurnStatusKind.Ready,

        ConversationTurnStatus.Dismissed              or
        ConversationTurnStatus.Resolved               => TurnStatusKind.Terminal,

        _                                             => TurnStatusKind.Neutral
    };

    /// <summary>Gets a value indicating whether an AI call is in flight (drives the busy indicator).</summary>
    public bool IsBusy => StatusKind == TurnStatusKind.Busy;

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
public sealed partial class ConversationTurnViewModel(IMediator mediator, TimeProvider clock) : ObservableObject
{
    [ObservableProperty] private ObservableCollection<TurnVm> _turns = [];
    [ObservableProperty] private SessionId? _activeSessionId;
    [ObservableProperty] private int _answerFontSize = 12;
    [ObservableProperty] private bool _isFollowUpEnabled;

    private static readonly TimeSpan ScreenCaptureGroupGap = TimeSpan.FromSeconds(8);
    private readonly ScreenCaptureAccumulator _screenAccumulator = new(ScreenCaptureGroupGap);
    private ConversationTurnId? _screenGroupTurnId;
    private CancellationTokenSource? _screenGenCts;
    private int _screenGenInFlight;

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
            $"[Error: {errorMessage}]", isError: true);
    }

    private static AnswerVersionVm CreateNewVersion(
        TurnVm turn, AnswerVersionId id, AnswerVersionType type, string text = "", bool isError = false)
    {
        foreach (var v in turn.AnswerVersions) v.IsLatest = false;
        var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
            { Text = text, IsLatest = true, IsError = isError };
        turn.AnswerVersions.Insert(0, version);
        turn.LatestVersion = version;
        return version;
    }

    /// <summary>Adds a new turn to the top of the list.</summary>
    public void AddTurn(ConversationTurnId id, string question)
        => Turns.Insert(0, new TurnVm(id, question));

    /// <summary>Clears all turns and resets the active session and any in-progress screen-capture group.</summary>
    public void Clear()
    {
        _screenGenCts?.Cancel();
        _screenGenCts?.Dispose();
        _screenGenCts = null;
        _screenGroupTurnId = null;
        _screenAccumulator.Reset();
        Turns.Clear();
        ActiveSessionId = null;
    }

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
        if (turn is null) return;
        if (_screenGroupTurnId == turn.Id) { _screenAccumulator.Reset(); _screenGroupTurnId = null; }
        if (ActiveSessionId is { } sid)
            await mediator.Send(new DismissTurnCommand(sid, turn.Id));
        Turns.Remove(turn);
    }

    /// <summary>Resolves the given turn and removes it from the list.</summary>
    [RelayCommand]
    private async Task ResolveAsync(TurnVm? turn)
    {
        if (turn is null) return;
        if (_screenGroupTurnId == turn.Id) { _screenAccumulator.Reset(); _screenGroupTurnId = null; }
        if (ActiveSessionId is { } sid)
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

    /// <summary>Captures the screen and (re)generates one answer over the accumulated captures of the
    /// current task. Captures taken while a generation is in flight, or within
    /// <see cref="ScreenCaptureGroupGap"/> of the previous generation finishing, refine the same card;
    /// a longer idle gap starts a new card. Each capture cancels any in-flight screen generation.</summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CaptureScreenAsync(SessionControlViewModel? sessionControl)
    {
        if (ActiveSessionId is not { } sid) return;
        if (sessionControl is null) return;

        var ocrResult = await mediator.Send(new CaptureScreenCommand());
        if (ocrResult.IsFailed) return;

        string[] interviewerLines = sessionControl.IncludeInterviewerContext
            ? _lastInterviewerLines
            : [];

        var now = clock.GetUtcNow();
        // A capture taken while a generation is still streaming belongs to the same task regardless of
        // how long the answer takes, so re-anchor the gap to "now" before deciding.
        if (_screenGenInFlight > 0)
            _screenAccumulator.Touch(now);

        var add = _screenAccumulator.Add(ocrResult.Value, now);

        // Supersede any in-flight screen generation for this group.
        _screenGenCts?.Cancel();
        _screenGenCts?.Dispose();
        _screenGenCts = new CancellationTokenSource();
        var token = _screenGenCts.Token;

        if (add.IsNewGroup)
            _screenGroupTurnId = null;

        _screenGenInFlight++;
        try
        {
            if (_screenGroupTurnId is null)
            {
                var created = await mediator.Send(new CreateScreenTurnCommand(sid), token);
                if (created.IsFailed) return;
                _screenGroupTurnId = created.Value;
            }

            var turnId = _screenGroupTurnId.Value;
            var turn = Turns.FirstOrDefault(t => t.Id == turnId);
            if (turn is not null)
            {
                turn.InitialQuestion = add.Count > 1
                    ? $"[Screen capture · {add.Count} screens]"
                    : "[Screen capture]";

                // Reset the displayed answer before this (re)generation streams. Chunks append to the
                // latest version, so without this a superseded partial answer would be concatenated
                // with the new one (the card shows two glued answers).
                var latest = turn.AnswerVersions.FirstOrDefault(v => v.IsLatest);
                if (latest is not null)
                    latest.Text = string.Empty;
            }

            await mediator.Send(new RegenerateAnswerWithScreenCommand(
                sid, turnId, add.CombinedOcr, sessionControl.ScreenAnalysisMode, interviewerLines), token);

            // Generation finished normally — restart the idle window from the completion moment so a
            // capture taken after reading a slow answer still joins this card.
            _screenAccumulator.Touch(clock.GetUtcNow());
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer capture (its CTS was cancelled). The winning capture takes over,
            // so this generation does not re-anchor the window.
        }
        finally
        {
            _screenGenInFlight--;
        }
    }

    private string[] _lastInterviewerLines = [];

    /// <summary>Updates the cached interviewer lines used as context for screen-based answer generation.</summary>
    /// <param name="lines">The most recent interviewer transcript lines.</param>
    public void UpdateInterviewerLines(IEnumerable<string> lines)
        => _lastInterviewerLines = lines.ToArray();
}
