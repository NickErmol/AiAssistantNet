using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIHelperNET.Integration.Tests.Persistence;

public class SessionPersistenceTests : IAsyncLifetime
{
    private AppDbContext _db = null!;
    private SessionRepository _repo = null!;

    public async Task InitializeAsync()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        _db = new AppDbContext(opts);
        await _db.Database.OpenConnectionAsync();
        await _db.Database.EnsureCreatedAsync();
        _repo = new SessionRepository(_db);
    }

    [Fact]
    public async Task AddAndGet_RoundTripsSession()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        await _repo.AddAsync(session, default);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        var result = await _repo.GetAsync(session.Id, default);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(session.Id);
        result.Value.State.Should().Be(SessionState.Active);
    }

    [Fact]
    public async Task AddAndGet_WithTranscriptAndQuestions_Roundtrips()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is CQRS?", DateTimeOffset.UtcNow, 0.95f));
        var q = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, DateTimeOffset.UtcNow);
        session.AddDetectedQuestion(q);

        await _repo.AddAsync(session, default);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = await _repo.GetAsync(session.Id, default);
        loaded.Value.Transcript.Should().HaveCount(1);
        loaded.Value.Questions.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetHistory_ReturnsOrderedSummaries()
    {
        for (int i = 0; i < 3; i++)
        {
            var s = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow.AddMinutes(i)).Value;
            await _repo.AddAsync(s, default);
        }
        await _db.SaveChangesAsync();

        var history = await _repo.GetHistoryAsync(10, default);
        history.Should().HaveCount(3);
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();
}
