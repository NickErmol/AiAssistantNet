using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

public class MigrationUpgradeTests
{
    [Fact]
    public async Task MigrateAsync_AddingColumn_UpgradesPopulatedDbWithoutDataLoss()
    {
        var opts = new DbContextOptionsBuilder<MigrationUpgradeTestContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        using var db = new MigrationUpgradeTestContext(opts);
        await db.Database.OpenConnectionAsync();   // keep :memory: alive across the two migrate calls

        // 1. Simulate an "old" install pinned at V1 (Widgets has no Note column).
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20000101000000_UpgradeV1");

        // 2. Insert a row using only the V1-shaped columns.
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Widgets (Id, Name) VALUES (1, 'old-row');");

        // 3. Upgrade to latest (V2 adds Note) — this is what threw "no such column" before migrations.
        await db.Database.MigrateAsync();

        // 4. New column is queryable AND the pre-existing row survived.
        db.ChangeTracker.Clear();
        var widget = await db.Widgets.SingleAsync();
        widget.Name.Should().Be("old-row");
        widget.Note.Should().BeNull();
    }
}
