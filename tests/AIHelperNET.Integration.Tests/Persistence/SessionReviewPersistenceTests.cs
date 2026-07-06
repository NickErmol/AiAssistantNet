using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIHelperNET.Integration.Tests.Persistence;

public class SessionReviewPersistenceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;
    private SessionReviewRepository _repo = null!;
    private SessionRepository _sessionRepo = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(opts);
        await _db.Database.OpenConnectionAsync();   // keep :memory: alive for the context lifetime
        await _db.Database.MigrateAsync();
        _repo = new SessionReviewRepository(_db);
        _sessionRepo = new SessionRepository(_db);
    }

    /// <summary>Helper: persist a session and return its id.</summary>
    private async Task<SessionId> CreateAndSaveSessionAsync()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        await _sessionRepo.AddAsync(session, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return session.Id;
    }

    [Fact]
    public async Task AddAndGet_RoundTripsAllProperties_IncludingGeneratedAtOffset()
    {
        var sessionId = await CreateAndSaveSessionAsync();
        // Use a non-UTC offset to verify the unix-ms conversion preserves the instant.
        var generatedAt = new DateTimeOffset(2026, 7, 6, 14, 30, 0, TimeSpan.FromHours(3));
        var review = SessionReview.Create(sessionId, "# Report\nSome content.", "claude-haiku-4-5", generatedAt);

        await _repo.AddAsync(review, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _repo.GetBySessionAsync(sessionId, default);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(review.Id);
        loaded.SessionId.Should().Be(sessionId);
        loaded.Markdown.Should().Be("# Report\nSome content.");
        loaded.ModelUsed.Should().Be("claude-haiku-4-5");
        // Unix-ms loses sub-millisecond precision; tolerate ±1 ms
        loaded.GeneratedAt.ToUnixTimeMilliseconds().Should().Be(generatedAt.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task Replace_ThenUpdate_AndSave_PersistsNewContent()
    {
        var sessionId = await CreateAndSaveSessionAsync();
        var original = SessionReview.Create(sessionId, "# Original", "claude-haiku-4-5", DateTimeOffset.UtcNow);

        await _repo.AddAsync(original, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _repo.GetBySessionAsync(sessionId, default);
        loaded.Should().NotBeNull();

        var laterTimestamp = DateTimeOffset.UtcNow.AddMinutes(5);
        loaded!.Replace("# Updated", "claude-sonnet-4-5", laterTimestamp);
        _repo.Update(loaded);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var reloaded = await _repo.GetBySessionAsync(sessionId, default);
        reloaded!.Markdown.Should().Be("# Updated");
        reloaded.ModelUsed.Should().Be("claude-sonnet-4-5");
        reloaded.GeneratedAt.ToUnixTimeMilliseconds().Should().Be(laterTimestamp.ToUnixTimeMilliseconds());
    }

    [Fact]
    public async Task InsertingSecondReviewForSameSession_ViolatesUniqueIndex()
    {
        var sessionId = await CreateAndSaveSessionAsync();
        var first = SessionReview.Create(sessionId, "# First", "claude-haiku-4-5", DateTimeOffset.UtcNow);
        var second = SessionReview.Create(sessionId, "# Second", "claude-haiku-4-5", DateTimeOffset.UtcNow.AddMinutes(1));

        await _repo.AddAsync(first, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        await _repo.AddAsync(second, default);
        var act = async () => await _db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();
}
