using AIHelperNET.Domain.Common;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Domain.Sessions;

/// <summary>Aggregate root representing a single interview copilot session.</summary>
public sealed class Session
{
    private readonly List<TranscriptItem> _transcript = [];
    private readonly List<DetectedQuestion> _questions = [];
    private readonly List<GeneratedAnswer> _answers = [];
    private readonly List<ConversationTurn> _turns = [];

    /// <summary>Unique identifier for this session.</summary>
    public SessionId Id { get; }

    /// <summary>When the session was started.</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>When the session was stopped, or <see langword="null"/> if still active.</summary>
    public DateTimeOffset? EndedAt { get; private set; }

    /// <summary>Current lifecycle state of the session.</summary>
    public SessionState State { get; private set; }

    /// <summary>The answer generation settings active for this session.</summary>
    public AnswerSettings AnswerSettings { get; private set; }

    /// <summary>The developer's code profile used to personalise answers.</summary>
    public CodeProfile CodeProfile { get; private set; }

    /// <summary>All transcript items captured during this session.</summary>
    public IReadOnlyList<TranscriptItem> Transcript => _transcript;

    /// <summary>All questions detected during this session.</summary>
    public IReadOnlyList<DetectedQuestion> Questions => _questions;

    /// <summary>All generated answers produced during this session.</summary>
    public IReadOnlyList<GeneratedAnswer> Answers => _answers;

    /// <summary>The session capture mode.</summary>
    public SessionMode Mode { get; private set; } = SessionMode.AudioAndScreen;

    /// <summary>Which audio sources are active.</summary>
    public AudioSourceMode AudioSource { get; private set; } = AudioSourceMode.Both;

    /// <summary>All conversation turns in this session.</summary>
    public IReadOnlyList<ConversationTurn> ConversationTurns => _turns;

    /// <summary>The most recent non-terminal conversation turn, or <see langword="null"/> if none.</summary>
    public ConversationTurn? ActiveTurn =>
        _turns.LastOrDefault(t =>
            t.Status is not ConversationTurnStatus.Dismissed
                     and not ConversationTurnStatus.Resolved);

    /// <summary>The most recent conversation turn overall (including terminal turns), or <see langword="null"/> if none.</summary>
    public ConversationTurn? LastTurn => _turns.Count == 0 ? null : _turns[^1];

    private Session(SessionId id, DateTimeOffset startedAt,
        AnswerSettings answerSettings, CodeProfile codeProfile)
    {
        Id = id;
        StartedAt = startedAt;
        State = SessionState.Active;
        AnswerSettings = answerSettings;
        CodeProfile = codeProfile;
    }

    /// <summary>Creates a new active <see cref="Session"/>.</summary>
    /// <param name="answerSettings">The answer settings to apply.</param>
    /// <param name="codeProfile">The developer's code profile.</param>
    /// <param name="now">The current timestamp used as the session start time.</param>
    public static DomainResult<Session> Create(
        AnswerSettings answerSettings, CodeProfile codeProfile, DateTimeOffset now)
    {
        if (answerSettings is null) return DomainResult.Fail<Session>("Answer settings required.");
        if (codeProfile is null) return DomainResult.Fail<Session>("Code profile required.");
        return DomainResult.Ok(new Session(SessionId.New(), now, answerSettings, codeProfile));
    }

    /// <summary>Adds a transcript item to the session.</summary>
    /// <param name="item">The transcript item to add.</param>
    public DomainResult AddTranscriptItem(TranscriptItem item)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot add transcript to a stopped session.");
        _transcript.Add(item);
        return DomainResult.Ok();
    }

    /// <summary>Adds a detected question to the session.</summary>
    /// <param name="question">The detected question to add.</param>
    public DomainResult AddDetectedQuestion(DetectedQuestion question)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot add question to a stopped session.");
        _questions.Add(question);
        return DomainResult.Ok();
    }

    /// <summary>Starts generating an answer for the specified question.</summary>
    /// <param name="questionId">The identifier of the question to answer.</param>
    /// <param name="now">The current timestamp used as the answer start time.</param>
    public DomainResult<GeneratedAnswer> StartAnswer(QuestionId questionId, DateTimeOffset now)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail<GeneratedAnswer>("Session is not active.");
        if (_questions.All(q => q.Id != questionId))
            return DomainResult.Fail<GeneratedAnswer>("Unknown question.");

        var answer = GeneratedAnswer.Create(questionId, now);
        _answers.Add(answer);
        return DomainResult.Ok(answer);
    }

    /// <summary>Replaces the current answer settings with <paramref name="settings"/>.</summary>
    /// <param name="settings">The new answer settings to apply.</param>
    public DomainResult UpdateAnswerSettings(AnswerSettings settings)
    {
        AnswerSettings = settings ?? throw new ArgumentNullException(nameof(settings));
        return DomainResult.Ok();
    }

    /// <summary>Replaces the current code profile with <paramref name="profile"/>.</summary>
    /// <param name="profile">The new code profile to apply.</param>
    public DomainResult UpdateCodeProfile(CodeProfile profile)
    {
        CodeProfile = profile ?? throw new ArgumentNullException(nameof(profile));
        return DomainResult.Ok();
    }

    /// <summary>Stops the session, recording the end time.</summary>
    /// <param name="now">The timestamp at which the session was stopped.</param>
    public DomainResult Stop(DateTimeOffset now)
    {
        if (State == SessionState.Stopped)
            return DomainResult.Fail("Session already stopped.");
        State = SessionState.Stopped;
        EndedAt = now;
        return DomainResult.Ok();
    }

    /// <summary>Changes the capture mode. Fails if the session is not active.</summary>
    /// <param name="mode">The new session capture mode.</param>
    /// <param name="audioSource">The new audio source selection.</param>
    public DomainResult ChangeMode(SessionMode mode, AudioSourceMode audioSource)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail("Cannot change mode on a stopped session.");
        Mode = mode;
        AudioSource = audioSource;
        return DomainResult.Ok();
    }

    /// <summary>Creates a new conversation turn for the given question. Fails if session is not active or question is unknown.</summary>
    /// <param name="questionId">The identifier of the question that opens this turn.</param>
    /// <param name="questionText">The text of the question.</param>
    /// <param name="now">The current timestamp.</param>
    public DomainResult<ConversationTurn> AddConversationTurn(
        QuestionId questionId, string questionText, DateTimeOffset now)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail<ConversationTurn>("Cannot add turn to a stopped session.");
        if (_questions.All(q => q.Id != questionId))
            return DomainResult.Fail<ConversationTurn>("Unknown question.");
        var turn = ConversationTurn.Create(Id, questionId, questionText, now);
        _turns.Add(turn);
        return DomainResult.Ok(turn);
    }

    /// <summary>
    /// Creates a new conversation turn in the <see cref="ConversationTurnStatus.CollectingQuestion"/> state
    /// for accumulating multi-fragment questions.
    /// </summary>
    /// <param name="questionId">The question ID for the first fragment.</param>
    /// <param name="firstFragment">The first transcript fragment to collect.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>The newly created turn, or a failure if the turn could not be created.</returns>
    public DomainResult<ConversationTurn> StartCollectingTurn(QuestionId questionId, string firstFragment, DateTimeOffset now)
    {
        if (State != SessionState.Active)
            return DomainResult.Fail<ConversationTurn>("Cannot add turn to a stopped session.");
        if (_questions.All(q => q.Id != questionId))
            return DomainResult.Fail<ConversationTurn>("Unknown question.");
        var turn = ConversationTurn.Create(Id, questionId, firstFragment, now);
        var result = turn.StartCollecting(firstFragment);
        if (!result.IsSuccess) return DomainResult<ConversationTurn>.Fail(result.Error);
        _turns.Add(turn);
        return DomainResult<ConversationTurn>.Ok(turn);
    }

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private Session() { }
#pragma warning restore CS8618
}
