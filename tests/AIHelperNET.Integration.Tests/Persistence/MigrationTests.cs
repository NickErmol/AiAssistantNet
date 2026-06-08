using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIHelperNET.Integration.Tests.Persistence;

public class MigrationTests
{
    /// <summary>
    /// Regression guard for issue #18: if an entity changes without a matching migration,
    /// the model diverges from the latest snapshot and this fails — forcing a migration.
    /// </summary>
    [Fact]
    public void Model_HasNoPendingChanges_AgainstLatestMigration()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(opts);

        db.Database.HasPendingModelChanges().Should().BeFalse(
            "every EF entity change must ship with an `dotnet ef migrations add`");
    }

    /// <summary>InitialSchema produces a schema that round-trips a Session aggregate (parity with EnsureCreated).</summary>
    [Fact]
    public async Task MigrateAsync_OnFreshDb_ProducesWorkingSchema()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new AppDbContext(opts);
        await db.Database.OpenConnectionAsync();   // keep :memory: alive for the context lifetime
        await db.Database.MigrateAsync();

        var repo = new SessionRepository(db);
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UtcNow).Value;
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is CQRS?", DateTimeOffset.UtcNow, 0.95f));
        await repo.AddAsync(session, default);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        var loaded = await repo.GetAsync(session.Id, default);
        loaded.IsSuccess.Should().BeTrue();
        loaded.Value.Transcript.Should().HaveCount(1);
    }
}
