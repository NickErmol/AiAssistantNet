using System.Text.Json;
using AIHelperNET.Domain.Common;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Domain.Sessions;

/// <summary>A detected question together with the AI answer versions generated for it.</summary>
public sealed class ConversationTurn
{
    private static readonly HashSet<ConversationTurnStatus> TerminalStatuses =
        [ConversationTurnStatus.Dismissed, ConversationTurnStatus.Resolved];

    private List<TranscriptItemId> _clarificationQuestionIds = [];
    private List<TranscriptItemId> _clarificationResponseIds = [];
    private readonly List<AnswerVersion> _answerVersions = [];

    // EF Core JSON bridge — maps private lists to TEXT columns
    internal string ClarificationQuestionIdsJson
    {
        get => JsonSerializer.Serialize(_clarificationQuestionIds.Select(id => id.Value));
        set => _clarificationQuestionIds = string.IsNullOrEmpty(value) ? [] :
            JsonSerializer.Deserialize<Guid[]>(value)!.Select(g => new TranscriptItemId(g)).ToList();
    }

    internal string ClarificationResponseIdsJson
    {
        get => JsonSerializer.Serialize(_clarificationResponseIds.Select(id => id.Value));
        set => _clarificationResponseIds = string.IsNullOrEmpty(value) ? [] :
            JsonSerializer.Deserialize<Guid[]>(value)!.Select(g => new TranscriptItemId(g)).ToList();
    }

    /// <summary>Unique identifier for this turn.</summary>
    public ConversationTurnId Id { get; }

    /// <summary>The session this turn belongs to.</summary>
    public SessionId SessionId { get; }

    /// <summary>The detected question that opened this turn.</summary>
    public QuestionId InitialQuestionId { get; }

    /// <summary>The text of the detected question.</summary>
    public string InitialQuestionText { get; }

    /// <summary>Current lifecycle status of this turn.</summary>
    public ConversationTurnStatus Status { get; private set; }

    /// <summary>IDs of transcript items the candidate asked for clarification.</summary>
    public IReadOnlyList<TranscriptItemId> ClarificationQuestionIds => _clarificationQuestionIds;

    /// <summary>IDs of transcript items that answered a clarification question.</summary>
    public IReadOnlyList<TranscriptItemId> ClarificationResponseIds => _clarificationResponseIds;

    /// <summary>All answer versions generated for this turn, oldest first.</summary>
    public IReadOnlyList<AnswerVersion> AnswerVersions => _answerVersions;

    /// <summary>When this turn was created.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>When this turn was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    private ConversationTurn(ConversationTurnId id, SessionId sessionId,
        QuestionId questionId, string questionText, DateTimeOffset now)
    {
        Id = id;
        SessionId = sessionId;
        InitialQuestionId = questionId;
        InitialQuestionText = questionText;
        Status = ConversationTurnStatus.Detected;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Creates a new <see cref="ConversationTurn"/> in the Detected state.</summary>
    /// <param name="sessionId">The session this turn belongs to.</param>
    /// <param name="questionId">The detected question that opened this turn.</param>
    /// <param name="questionText">The text of the detected question.</param>
    /// <param name="now">The current timestamp.</param>
    /// <returns>A new <see cref="ConversationTurn"/> in the <see cref="ConversationTurnStatus.Detected"/> state.</returns>
    public static ConversationTurn Create(
        SessionId sessionId, QuestionId questionId, string questionText, DateTimeOffset now)
        => new(ConversationTurnId.New(), sessionId, questionId, questionText, now);

    /// <summary>Transitions the turn to the next status. Fails if already in a terminal state.</summary>
    /// <param name="next">The target status.</param>
    /// <returns>A <see cref="DomainResult"/> indicating success or failure.</returns>
    public DomainResult TransitionTo(ConversationTurnStatus next)
    {
        if (TerminalStatuses.Contains(Status))
            return DomainResult.Fail($"Cannot transition from terminal status {Status}.");
        Status = next;
        UpdatedAt = DateTimeOffset.UtcNow;
        return DomainResult.Ok();
    }

    /// <summary>Appends an answer version to this turn.</summary>
    /// <param name="version">The answer version to append.</param>
    public void AddAnswerVersion(AnswerVersion version)
    {
        _answerVersions.Add(version);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the candidate asked a clarification question.</summary>
    /// <param name="itemId">The transcript item ID of the clarification question.</param>
    public void AttachClarificationQuestion(TranscriptItemId itemId)
    {
        _clarificationQuestionIds.Add(itemId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the interviewer provided a clarification response.</summary>
    /// <param name="itemId">The transcript item ID of the clarification response.</param>
    public void AttachClarificationResponse(TranscriptItemId itemId)
    {
        _clarificationResponseIds.Add(itemId);
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the turn as dismissed.</summary>
    public void Dismiss()
    {
        Status = ConversationTurnStatus.Dismissed;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks the turn as resolved.</summary>
    public void Resolve()
    {
        Status = ConversationTurnStatus.Resolved;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

#pragma warning disable CS8618
    private ConversationTurn() { }
#pragma warning restore CS8618
}
