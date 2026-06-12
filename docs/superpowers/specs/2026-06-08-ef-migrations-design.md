# EF Core Migrations — Design (Issue #18)

**Date:** 2026-06-08
**Branch:** `feature/ef-migrations`
**Issue:** https://github.com/NickErmol/AiAssistantNet/issues/18

## Problem

The app creates its SQLite schema with `EnsureCreatedAsync()` (`src/AIHelperNET.App/App.xaml.cs:36`) and has **no EF Core migrations**. `EnsureCreatedAsync()` creates the DB only when absent and never alters an existing schema, so whenever an EF entity gains/loses a column, existing installs crash at runtime with `SQLite Error 1: 'no such column'`. New installs are fine; upgrades are not. `CLAUDE.md` and project notes wrongly claim `MigrateAsync()` is already used.

## Goal

Replace `EnsureCreatedAsync()` with a real migrations pipeline so schema changes upgrade existing installs instead of crashing. Establish the per-feature migration workflow and lock it in with automated tests.

## Decisions (settled during brainstorming)

- **Existing DBs:** one-time reset (documented). Existing `sessions.db` files have no `__EFMigrationsHistory`; rather than add auto-baseline/stamping code, adopting migrations requires deleting the existing `sessions.db` once. Accepts loss of current local session history.
- **Verification:** automated upgrade integration test (plus parity guard + fresh-apply round-trip).
- **E2E harness:** `InterviewHost` switches from `EnsureCreatedAsync()` to `MigrateAsync()` for production parity.

## Components

### 1. Design-time factory — `src/AIHelperNET.Infrastructure/Persistence/AppDbContextFactory.cs` (new)

`IDesignTimeDbContextFactory<AppDbContext>`. The WPF App builds its host inside `OnStartup`, which `dotnet ef` cannot discover, so this factory is required for `dotnet ef migrations add` to instantiate the context. It builds `DbContextOptions<AppDbContext>` with `UseSqlite` using the same connection-string convention as `DependencyInjection` (`AppPaths.DatabaseFile`). Migration scaffolding never connects to the DB, so the path only needs to name the provider.

### 2. Baseline migration — `src/AIHelperNET.Infrastructure/Persistence/Migrations/<ts>_InitialSchema.cs` (+ model snapshot)

Generated via:

```
dotnet ef migrations add InitialSchema \
  --project src/AIHelperNET.Infrastructure \
  --startup-project src/AIHelperNET.App \
  --output-dir Persistence/Migrations
```

Captures the entire current owned-entity model (Sessions, Transcript, Questions, Answers, ConversationTurns, AnswerVersions — including `BoundaryRole`, `LastUpdateReason`, and the JSON-bridge columns with their `[]` defaults). Reviewed to confirm it reproduces exactly what `EnsureCreated` produced.

### 3. Startup switch — `src/AIHelperNET.App/App.xaml.cs:36`

`await db.Database.EnsureCreatedAsync();` → `await db.Database.MigrateAsync();`

### 4. E2E harness switch — `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs:77`

`await db.Database.EnsureCreatedAsync();` → `await db.Database.MigrateAsync();`. The harness keeps a keep-alive `SqliteConnection` open for the in-memory DB, so `MigrateAsync()` applies cleanly. Update the class comment that says "schema created via EnsureCreatedAsync".

## Behavior for existing DBs (one-time reset)

Existing `sessions.db` files (local dev + `D:\AIHelperNET\`) were created by `EnsureCreated` and have no `__EFMigrationsHistory`. `MigrateAsync` would try to re-create existing tables and fail. **Resolution:** delete the existing `sessions.db` once when adopting migrations. No startup detection or auto-baseline code. Documented in CLAUDE.md, the `add-ef-migration` skill, and the migration-strategy memory.

## Tests — `tests/AIHelperNET.Integration.Tests/Persistence/`

1. **Upgrade path (core regression test):** a minimal dedicated `MigrationUpgradeTestContext` defined in the test project with two migrations V1 → V2, where V2 adds a nullable column. Apply V1 to a temp-file SQLite DB, insert a row, run `MigrateAsync()` to V2, assert the new column is queryable **and the pre-existing row survives** with no `no such column`. Proves the upgrade mechanism the app now relies on, decoupled from product schema churn.
2. **Model/migration parity guard:** `Assert.False(ctx.Database.HasPendingModelChanges())` against the real `AppDbContext` — fails the suite if anyone changes an entity without adding a migration. This is what keeps the bug class from returning.
3. **Fresh-apply round-trip:** temp-file DB, `MigrateAsync()`, then save & reload a Session aggregate — proves `InitialSchema` is complete (parity with the old `EnsureCreated`).

## Docs

- **CLAUDE.md** — correct the "Key non-obvious facts": migrations are real now, `MigrateAsync()` at startup, one-time DB reset on adoption.
- **`.claude/skills/add-ef-migration`** — flip from PLANNED to active; keep the per-feature workflow steps.
- **`project-db-migration-strategy` memory** — flip status from PLANNED/NOT-IMPLEMENTED to DONE.

## Per-feature workflow (ongoing, after this lands)

When a feature branch adds/changes an EF entity:
1. Make the entity change.
2. `dotnet ef migrations add <DescriptiveName> --project src/AIHelperNET.Infrastructure --startup-project src/AIHelperNET.App --output-dir Persistence/Migrations`
3. Review the generated migration touches only what changed.
4. Commit migration files alongside the entity change.
5. `MigrateAsync()` at startup applies it on all installs.

The parity-guard test (#2) enforces step 2 was not skipped.

## Out of scope

Auto-baseline/stamping, production data preservation, switching `SessionPersistenceTests` off `EnsureCreated` (optional, not required), and any schema change beyond the throwaway test column in the upgrade test.

## Verification before done

- `dotnet build` clean (TreatWarningsAsErrors).
- `dotnet test` green — full suite incl. the three new tests (2 pre-existing UITest failures excepted).
- Manual run of the app on a freshly-reset DB confirming startup + a session round-trip.
