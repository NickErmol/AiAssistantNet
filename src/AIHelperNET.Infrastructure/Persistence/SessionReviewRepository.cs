using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class SessionReviewRepository(AppDbContext db) : ISessionReviewRepository
{
    public async Task<SessionReview?> GetBySessionAsync(SessionId sessionId, CancellationToken ct)
        => await db.SessionReviews
            .FirstOrDefaultAsync(r => r.SessionId == sessionId, ct);

    public async Task AddAsync(SessionReview review, CancellationToken ct)
        => await db.SessionReviews.AddAsync(review, ct);

    public void Update(SessionReview review)
        => db.SessionReviews.Update(review);
}
