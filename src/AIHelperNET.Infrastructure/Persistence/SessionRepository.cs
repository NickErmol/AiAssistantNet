using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class SessionRepository(AppDbContext db) : ISessionRepository
{
    public async Task<Result<Session>> GetAsync(SessionId id, CancellationToken ct)
    {
        var session = await db.Sessions
            .Include(s => s.Transcript)
            .Include(s => s.Questions)
            .Include(s => s.Answers)
            .Include(s => s.ConversationTurns)
                .ThenInclude(t => t.AnswerVersions)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        return session is null
            ? Result.Fail($"Session {id.Value} not found.")
            : Result.Ok(session);
    }

    public async Task AddAsync(Session session, CancellationToken ct)
        => await db.Sessions.AddAsync(session, ct);

    public async Task<IReadOnlyList<SessionSummaryDto>> GetHistoryAsync(int take, CancellationToken ct)
    {
        return await db.Sessions
            .OrderByDescending(s => s.StartedAt)
            .Take(take)
            .Select(s => new SessionSummaryDto(
                s.Id,
                s.StartedAt,
                s.EndedAt,
                s.State,
                s.Questions.Count,
                s.Answers.Count))
            .ToListAsync(ct);
    }

    public void Update(Session session)
        => db.Sessions.Update(session);

    public async Task<SessionDetailDto?> GetDetailAsync(SessionId id, CancellationToken ct)
    {
        var session = await db.Sessions
            .Include(s => s.Transcript)
            .Include(s => s.ConversationTurns)
                .ThenInclude(t => t.AnswerVersions)
            .FirstOrDefaultAsync(s => s.Id == id, ct);

        if (session is null) return null;

        var transcript = session.Transcript
            .OrderBy(i => i.Timestamp)
            .Select(i => new TranscriptItemDto(
                i.Id,
                i.Speaker,
                i.Text,
                i.Timestamp,
                i.Confidence))
            .ToList();

        var answers = session.ConversationTurns
            .OrderBy(t => t.CreatedAt)
            .SelectMany(turn => turn.AnswerVersions
                .OrderByDescending(v => v.CreatedAt)
                .Take(1)
                .Select(v => new AnswerDto(
                    turn.InitialQuestionText,
                    v.Text,
                    v.CreatedAt)))
            .ToList();

        return new SessionDetailDto(
            session.Id,
            session.StartedAt,
            session.EndedAt,
            session.Mode.ToString(),
            transcript,
            answers);
    }
}
