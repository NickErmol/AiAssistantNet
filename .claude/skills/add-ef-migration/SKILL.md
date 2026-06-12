---
description: Create an EF Core migration when adding or changing an entity column/table in AIHelperNET
---

# Add EF Migration

Run whenever an EF entity changes (new property, new entity, renamed/dropped column/type). The
schema is applied at startup via `MigrateAsync()`, so a change without a migration crashes existing
installs with "no such column".

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
Open the new file in `src/AIHelperNET.Infrastructure/Persistence/Migrations/` and confirm it touches
ONLY what changed. SQLite rebuilds the table for rename/drop/type-change — that is expected.

## 4 — Verify
```powershell
dotnet build
dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~MigrationTests"
```
The `Model_HasNoPendingChanges_AgainstLatestMigration` parity guard fails if the model still has
un-migrated changes.

## 5 — Commit
Commit the migration + updated `AppDbContextModelSnapshot.cs` alongside the entity change.
`MigrateAsync()` at startup applies it on all installs.
