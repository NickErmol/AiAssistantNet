using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

/// <summary>A trivial entity used only to exercise the migrate-upgrade path. `Note` is added by UpgradeV2.</summary>
public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Note { get; set; }
}

/// <summary>
/// Standalone context with two hand-written migrations (V1 → V2). Lets us prove that
/// <c>MigrateAsync()</c> upgrades a populated DB by adding a column without data loss —
/// the exact scenario that crashed before issue #18.
/// </summary>
public sealed class MigrationUpgradeTestContext(DbContextOptions<MigrationUpgradeTestContext> options)
    : DbContext(options)
{
    public DbSet<Widget> Widgets => Set<Widget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<Widget>().HasKey(x => x.Id);
}
