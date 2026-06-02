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

#pragma warning disable CS8618 // EF Core parameterless constructor — properties set by materialiser
    private Session() { }
#pragma warning restore CS8618
}
