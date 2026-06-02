using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Full projection of a session aggregate.</summary>
/// <param name="Id">Session identifier.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="EndedAt">When the session ended, or null if still active.</param>
/// <param name="State">Current session state.</param>
/// <param name="AnswerSettings">Active answer settings.</param>
/// <param name="CodeProfile">Active code profile.</param>
/// <param name="Transcript">All transcript items.</param>
/// <param name="Questions">All detected questions.</param>
/// <param name="Answers">All generated answers.</param>
public sealed record SessionDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SessionState State,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    IReadOnlyList<TranscriptItemDto> Transcript,
    IReadOnlyList<DetectedQuestionDto> Questions,
    IReadOnlyList<GeneratedAnswerDto> Answers);

/// <summary>Projection of a single transcript item.</summary>
/// <param name="Id">Item identifier.</param>
/// <param name="Speaker">Who spoke.</param>
/// <param name="Text">Transcribed text.</param>
/// <param name="Timestamp">When it was captured.</param>
/// <param name="Confidence">Transcription confidence.</param>
public sealed record TranscriptItemDto(
    TranscriptItemId Id, Speaker Speaker, string Text,
    DateTimeOffset Timestamp, float Confidence);

/// <summary>Projection of a detected question.</summary>
/// <param name="Id">Question identifier.</param>
/// <param name="Text">Question text.</param>
/// <param name="Source">Source of detection.</param>
/// <param name="DetectedAt">When the question was detected.</param>
public sealed record DetectedQuestionDto(
    QuestionId Id, string Text, QuestionSource Source, DateTimeOffset DetectedAt);

/// <summary>Projection of a generated answer.</summary>
/// <param name="Id">Answer identifier.</param>
/// <param name="QuestionId">The question this answers.</param>
/// <param name="StartedAt">When generation started.</param>
/// <param name="CompletedAt">When generation finished, or null if still streaming.</param>
/// <param name="Status">Current status.</param>
/// <param name="Content">Accumulated answer content.</param>
public sealed record GeneratedAnswerDto(
    AnswerId Id, QuestionId QuestionId, DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt, AnswerStatus Status, string Content);
