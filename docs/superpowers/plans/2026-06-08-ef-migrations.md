# EF Core Migrations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `EnsureCreatedAsync()` with a real EF Core migrations pipeline so schema changes upgrade existing installs instead of crashing with `no such column`.

**Architecture:** Add a design-time `IDesignTimeDbContextFactory` so `dotnet ef` can run against the WPF startup project, generate a baseline `InitialSchema` migration, switch startup (and the E2E harness) to `MigrateAsync()`, and lock the fix in with three integration tests (upgrade-path regression, model/migration parity guard, fresh-apply round-trip). Existing `sessions.db` files are reset once (no auto-baseline code).

**Tech Stack:** .NET 10, EF Core 10.0.8 (Sqlite), `dotnet-ef` 10.0.0, xUnit + FluentAssertions.

**Spec:** `docs/superpowers/specs/2026-06-08-ef-migrations-design.md`

---

## File Structure

- `src/AIHelperNET.Infrastructure/Persistence/AppDbContextFactory.cs` — NEW: design-time factory (EF tooling only).
- `src/AIHelperNET.Infrastructure/Persistence/Migrations/*.cs` — NEW (generated): `InitialSchema` migration + `AppDbContextModelSnapshot`.
- `src/AIHelperNET.App/App.xaml.cs:36` — MODIFY: `EnsureCreatedAsync` → `MigrateAsync`.
- `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs` — MODIFY: `EnsureCreatedAsync` → `MigrateAsync` + comment fixes.
- `tests/AIHelperNET.Integration.Tests/Persistence/MigrationTests.cs` — NEW: parity guard + fresh-apply round-trip.
- `tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/*.cs` — NEW: dedicated two-migration test context + upgrade-path test.
- `CLAUDE.md` — MODIFY: add a Database-migrations fact.
- `.claude/skills/add-ef-migration/SKILL.md` — NEW: the per-feature migration workflow skill.

---

## Task 1: Design-time factory + baseline `InitialSchema` migration

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Persistence/AppDbContextFactory.cs`
- Create (generated): `src/AIHelperNET.Infrastructure/Persistence/Migrations/<ts>_InitialSchema.cs` + `AppDbContextModelSnapshot.cs`

- [ ] **Step 1: Create the design-time factory**

`src/AIHelperNET.Infrastructure/Persistence/AppDbContextFactory.cs`:

```csharp
using AIHelperNET.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIHelperNET.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can instantiate <see cref="AppDbContext"/> without
/// running the WPF App host (which builds its DI host inside <c>OnStartup</c> and is therefore
/// not discoverable by EF tooling). Used only by migration scaffolding — never at runtime.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={AppPaths.DatabaseFile}")
            .Options;
        return new AppDbContext(options);
    }
}
```

- [ ] **Step 2: Verify the factory compiles**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Generate the baseline migration**

Run:
```bash
dotnet ef migrations add InitialSchema --project src/AIHelperNET.Infrastructure --startup-project src/AIHelperNET.App --output-dir Persistence/Migrations
```
Expected: `Done.` and new files under `src/AIHelperNET.Infrastructure/Persistence/Migrations/`.

- [ ] **Step 4: Review the generated migration**

Open the `<ts>_InitialSchema.cs` file. Confirm `Up()` creates: `Sessions` (with owned `AnswerSettings_*`, `CodeProfile_*`, `Mode`, `AudioSource`), `Session_Transcript` (incl. `BoundaryRole` default), `Session_Questions`, `Session_Answers` (incl. `Content`), `ConversationTurns` (incl. `ClarificationQuestionIds`/`ClarificationResponseIds`/`QuestionFragments` TEXT defaults `[]`, `LastUpdateReason`), and the `AnswerVersions` owned table. If any current column is missing, the model mapping is the source of truth — do NOT hand-edit the migration; fix the mapping and regenerate.

- [ ] **Step 5: Verify full build still clean**

Run: `dotnet build`
Expected: Build succeeded, 0 errors (TreatWarningsAsErrors is on).

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.Infrastructure/Persistence/AppDbContextFactory.cs src/AIHelperNET.Infrastructure/Persistence/Migrations
git commit -m "feat(db): add design-time factory and InitialSchema baseline migration"
```

---

## Task 2: Switch startup and E2E harness to `MigrateAsync`

**Files:**
- Modify: `src/AIHelperNET.App/App.xaml.cs:36`
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs` (line ~77 + comments at lines ~15 and ~42)

- [ ] **Step 1: Switch the app startup**

In `src/AIHelperNET.App/App.xaml.cs`, replace:

```csharp
            await db.Database.EnsureCreatedAsync();
```

with:

```csharp
            await db.Database.MigrateAsync();
```

- [ ] **Step 2: Switch the E2E harness**

In `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs`, replace:

```csharp
            await db.Database.EnsureCreatedAsync();
```

with:

```csharp
            await db.Database.MigrateAsync();
```

Then update the two stale comments that mention `EnsureCreatedAsync`:
- The class-level comment (~line 15): "in-memory SQLite database (schema created via **migrations**, matching the app)".
- The method summary (~line 42): "Builds the host, opens the shared in-memory DB, and **applies migrations**."

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the E2E + persistence suites (they now exercise MigrateAsync)**

Run: `dotnet test tests/AIHelperNET.Integration.Tests`
Expected: PASS (all Integration tests green; the `InterviewHost*` tests now build schema via migrations).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/App.xaml.cs tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs
git commit -m "feat(db): apply migrations at startup and in E2E harness"
```

---

## Task 3: Parity guard + fresh-apply round-trip tests

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/MigrationTests.cs`

- [ ] **Step 1: Write the tests**

`tests/AIHelperNET.Integration.Tests/Persistence/MigrationTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~MigrationTests"`
Expected: PASS (2 tests). If `Model_HasNoPendingChanges` FAILS, the migration from Task 1 is incomplete — regenerate it; do not weaken the assertion.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Persistence/MigrationTests.cs
git commit -m "test(db): parity guard and fresh-apply round-trip for migrations"
```

---

## Task 4: Upgrade-path regression test (dedicated two-migration context)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/MigrationUpgradeTestContext.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/UpgradeV1.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/UpgradeV2.cs`
- Create: `tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/MigrationUpgradeTests.cs`

This test exercises the actual EF migrate-upgrade mechanism (apply column-adding migration to a populated DB) decoupled from the product schema, so it never needs churn when `AppDbContext` evolves. Runtime `Migrate()` discovers `Migration` classes by attribute — no model snapshot is required.

- [ ] **Step 1: Create the test context and entity**

`tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/MigrationUpgradeTestContext.cs`:

```csharp
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
```

- [ ] **Step 2: Create migration V1 (creates the table without `Note`)**

`tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/UpgradeV1.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

[DbContext(typeof(MigrationUpgradeTestContext))]
[Migration("20000101000000_UpgradeV1")]
public sealed class UpgradeV1 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.CreateTable(
            name: "Widgets",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false),
                Name = table.Column<string>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Widgets", x => x.Id));

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable("Widgets");
}
```

- [ ] **Step 3: Create migration V2 (adds the `Note` column)**

`tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/UpgradeV2.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AIHelperNET.Integration.Tests.Persistence.UpgradeFixture;

[DbContext(typeof(MigrationUpgradeTestContext))]
[Migration("20000101000001_UpgradeV2")]
public sealed class UpgradeV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<string>(name: "Note", table: "Widgets", nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "Note", table: "Widgets");
}
```

- [ ] **Step 4: Write the upgrade test**

`tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture/MigrationUpgradeTests.cs`:

```csharp
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
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~MigrationUpgradeTests"`
Expected: PASS (1 test). If EF reports it cannot find migrations, confirm both classes carry `[DbContext(typeof(MigrationUpgradeTestContext))]` and unique `[Migration(...)]` ids.

- [ ] **Step 6: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/Persistence/UpgradeFixture
git commit -m "test(db): upgrade-path regression test for add-column migration"
```

---

## Task 5: Docs — CLAUDE.md fact + add-ef-migration skill

**Files:**
- Modify: `CLAUDE.md` (under `## Key non-obvious facts`)
- Create: `.claude/skills/add-ef-migration/SKILL.md`

- [ ] **Step 1: Add the migrations fact to CLAUDE.md**

In `CLAUDE.md`, immediately after the `**Data root**: ...` paragraph under `## Key non-obvious facts`, insert:

```markdown
**Database migrations**: schema is managed by EF Core migrations in `src/AIHelperNET.Infrastructure/Persistence/Migrations/`, applied at startup via `MigrateAsync()` (`App.xaml.cs`) — NOT `EnsureCreated`. Any EF entity change must ship a migration (`dotnet ef migrations add <Name> --project src/AIHelperNET.Infrastructure --startup-project src/AIHelperNET.App --output-dir Persistence/Migrations`) committed alongside the change; the `MigrationTests` parity guard fails the build otherwise. A design-time factory (`AppDbContextFactory`) lets the tooling run against the WPF startup project. Adopting migrations required a one-time delete of pre-existing `sessions.db` files (created before `__EFMigrationsHistory` existed).
```

- [ ] **Step 2: Create the add-ef-migration skill**

`.claude/skills/add-ef-migration/SKILL.md`:

```markdown
---
description: Create an EF Core migration when adding or changing an entity column/table in AIHelperNET
---

# Add EF Migration

Run whenever an EF entity changes (new property, new entity, renamed/dropped column/type).

## 1 — Make the entity change first
Edit the entity and any `AppDbContext.OnModelCreating` mapping.

## 2 — Generate the migration
```powershell
cd D:\work\AIHelperNET
dotnet ef migrations add <DescriptiveName> `
  --project src/AIHelperNET.Infrastructure `
  --startup-project src/AIHelperNET.App `
  --output-dir Persistence/Migrations
```

## 3 — Review
Open the new file in `src/AIHelperNET.Infrastructure/Persistence/Migrations/` and confirm it touches ONLY what changed. SQLite rebuilds the table for rename/drop/type-change — that is expected.

## 4 — Verify
```powershell
dotnet build
dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~MigrationTests"
```
The parity-guard test fails if the model still has un-migrated changes.

## 5 — Commit
Commit the migration + snapshot files alongside the entity change. `MigrateAsync()` at startup applies it on all installs.
```

- [ ] **Step 3: Verify docs build (no code impact) and commit**

```bash
git add CLAUDE.md .claude/skills/add-ef-migration/SKILL.md
git commit -m "docs(db): document migration workflow; add add-ef-migration skill"
```

---

## Final verification (before finishing the branch)

- [ ] **Step 1: Clean build**

Run: `dotnet build`
Expected: Build succeeded, 0 warnings/errors.

- [ ] **Step 2: Full test suite**

Run: `dotnet test`
Expected: All green except the two long-known UITest failures (`BothMode_MicAndSystemDotsActive`, `ScreenCaptureTests.Capture_WithTestImage_ProducesTurnCard`). The three new migration tests pass.

- [ ] **Step 3: Manual smoke (one-time DB reset path)**

Delete the local `sessions.db` (under `D:\AIHelperNET\` or `%LocalAppData%\AIHelperNET\`), launch the app (run-aihelper skill), confirm it starts, start a session, and confirm a turn round-trips. This validates `MigrateAsync` creates the schema fresh and the reset workflow.

- [ ] **Step 4: Hand off to finishing-a-development-branch** to open the PR to `develop`.

---

## Post-merge (not repo commits — handle in the session)

Update memory to reflect reality:
- `project-db-migration-strategy.md`: flip status PLANNED → DONE.
- `MEMORY.md` / handoff: mark Issue #18 done; note `add-ef-migration` skill now actually exists.
