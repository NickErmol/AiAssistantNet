using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Application.Sessions;

/// <summary>Maps <see cref="Session"/> aggregates to their DTO projections.</summary>
public sealed class SessionMapper
{
    /// <summary>Projects a full <see cref="Session"/> to a <see cref="SessionDto"/>.</summary>
    /// <param name="session">The session to project.</param>
    public static SessionDto ToDto(Session session) => new(
        session.Id,
        session.StartedAt,
        session.EndedAt,
        session.State,
        session.AnswerSettings,
        session.CodeProfile,
        session.Transcript.Select(t => new TranscriptItemDto(t.Id, t.Speaker, t.Text, t.Timestamp, t.Confidence)).ToList(),
        session.Questions.Select(q => new DetectedQuestionDto(q.Id, q.Text, q.Source, q.DetectedAt)).ToList(),
        session.Answers.Select(a => new GeneratedAnswerDto(a.Id, a.QuestionId, a.StartedAt, a.CompletedAt, a.Status, a.Content)).ToList())
    {
        Mode        = session.Mode,
        AudioSource = session.AudioSource
    };

    /// <summary>Projects a <see cref="Session"/> to a lightweight <see cref="SessionSummaryDto"/>.</summary>
    /// <param name="session">The session to summarise.</param>
    public static SessionSummaryDto ToSummary(Session session) => new(
        session.Id,
        session.StartedAt,
        session.EndedAt,
        session.State,
        session.Questions.Count,
        session.Answers.Count);
}
