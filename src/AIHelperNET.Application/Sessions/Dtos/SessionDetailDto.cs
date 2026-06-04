using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>A Q&amp;A pair returned as part of session detail.</summary>
public sealed record AnswerDto(
    string QuestionText,
    string AnswerText,
    DateTimeOffset CreatedAt);

/// <summary>Full session data including transcript and answers, used by the history panel.</summary>
public sealed record SessionDetailDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string Mode,
    IReadOnlyList<TranscriptItemDto> Transcript,
    IReadOnlyList<AnswerDto> Answers);
