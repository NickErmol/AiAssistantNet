using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Lightweight summary of a session for history lists.</summary>
/// <param name="Id">Session identifier.</param>
/// <param name="StartedAt">When the session started.</param>
/// <param name="EndedAt">When the session ended, or null if still active.</param>
/// <param name="State">Current session state.</param>
/// <param name="QuestionCount">Total number of detected questions.</param>
/// <param name="AnswerCount">Total number of generated answers.</param>
public sealed record SessionSummaryDto(
    SessionId Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    SessionState State,
    int QuestionCount,
    int AnswerCount);
