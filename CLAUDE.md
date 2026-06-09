# CLAUDE.md

Guidance for Claude Code working in this repository. Treat it as an **index** — point to
skills and `docs/` for detail rather than restating it here.

## Tech stack

- **.NET 10**, C# `latest`, nullable enabled. `Domain`/`Application` target `net10.0`
  (platform-neutral); `App`/`Infrastructure` target `net10.0-windows`.
- **UI:** WPF + CommunityToolkit.Mvvm; MS Generic Host (`Microsoft.Extensions.Hosting`) for DI/lifetime.
- **CQRS:** [Mediator](https://github.com/martinothamar/Mediator) 3.x (source generator).
- **Validation:** FluentValidation · **Mapping:** Riok.Mapperly (source-gen) · **Results:** FluentResults.
- **Persistence:** EF Core 10 + SQLite (migrations).
- **Audio/AI:** NAudio (capture) · Whisper.net (transcription, +Vulkan GPU runtime) · ONNX Runtime
  (Silero VAD) · OllamaSharp + a custom Claude HTTP/SSE client (answers).
- **Secrets:** AdysTech.CredentialManager (Windows Credential Manager). **Logging:** Serilog (file sink).
- **Tests:** xUnit · FluentAssertions · NSubstitute · FlaUI/UIA3 (UI automation) · NetArchTest (layering guards).

## Commands

```bash
dotnet build                                               # full solution
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj   # WPF app only
dotnet test                                                # all tests
dotnet test tests/AIHelperNET.Domain.Tests                 # single project
dotnet test --filter "FullyQualifiedName~QuestionDetector" # single test
```

`Directory.Build.props` enforces `TreatWarningsAsErrors`, nullable, latest C#, and XML docs on all projects.

## Architecture

Clean/Hexagonal + DDD + CQRS. Dependencies point **inward** toward `Domain`
(diagram: `docs/architecture/project-dependencies.md`).

```
Domain         — Session aggregate, value objects, DomainResult<T>, strongly-typed ID wrappers
Application    — CQRS handlers, port interfaces (Abstractions/), TranscriptPipelineService
Infrastructure — EF Core/SQLite, NAudio, Whisper.net, Claude/Ollama HTTP, Windows APIs
App            — WPF, ViewModels, sink wiring, MS Generic Host (composition root)
```

**Port interfaces** (`Application/Abstractions/`, implemented in `Infrastructure`):
`ITranscriptSink`, `IAnswerStreamSink`, `IAudioCaptureService`, `ITranscriptionService`,
`IAnswerProvider`, `ISessionRepository`, `IUnitOfWork`, `ISettingsStore`, `ISecretStore`.

## Conventions

- **Add a handler:** implement `ICommandHandler<TCommand>` / `IQueryHandler<TQuery, TResult>` —
  Mediator's source generator registers it automatically. FluentValidation validators are
  auto-discovered from the Application assembly.
- **Domain operations never throw** — they return `DomainResult<T>`. Entity IDs are strongly-typed
  `Guid` wrappers (`SessionId`, `ConversationTurnId`, …).
- **Object mapping** uses Riok.Mapperly source-gen (`[Mapper]` partials) — don't hand-roll mappers.
- **EF entity change ⇒ ship a migration in the same commit** (the `MigrationTests` parity guard fails
  the build otherwise). Use the `add-ef-migration` skill.
- **Gitflow:** `feature/*` → `develop` → `release/*` → `master`. Never commit directly to `master`.
- Layering is enforced by **NetArchTest** rules in the integration tests — don't add a reference that
  points a layer outward.

## Key non-obvious facts (gotchas)

**Data root:** `D:\AIHelperNET\` when D: is ready, else `%LocalAppData%\AIHelperNET\`. Models, SQLite
DB, settings JSON, and logs all live there. Transcripts are sensitive — see the standing security rule.

**Migrations apply at startup** via `MigrateAsync()` in `App.xaml.cs` (NOT `EnsureCreated`). A
design-time factory (`AppDbContextFactory`) lets the EF tooling run against the WPF startup project;
on failure the app shows a dialog and exits cleanly. Tests/CI use in-memory SQLite so they're
unaffected. Full workflow lives in the `add-ef-migration` skill.

**Sink wiring:** `TranscriptSink`/`AnswerStreamSink` are singletons; handlers are set in
`App.OnStartup` via `sink.SetHandler(...)` *after* the host starts but *before* `overlay.Show()`,
and marshal to the UI thread with `Dispatcher.BeginInvoke`.

**WPF binding:** never bind `ItemsSource` through a non-`INotifyPropertyChanged` intermediate. Bind
`DataContext="{Binding Transcript}"` on the sub-element so the ItemsControl binds `{Binding Items}` in
a single hop on a proper `ObservableObject`. A two-hop path through a plain class silently drops
`CollectionChanged` subscriptions.

**WhisperModelProvider:** `SemaphoreSlim(1)` guards only the initial load/download. Once a
`WhisperFactory` is cached, processors built from it run in parallel (`factory.CreateBuilder().Build()`
is independent per call).

## Standing rules

Always-loaded project rules (keep in mind on every change):

@.claude/rules/security.md
