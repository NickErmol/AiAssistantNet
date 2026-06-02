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
}
