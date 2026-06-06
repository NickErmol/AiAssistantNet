# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                                              # full solution
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj  # WPF app only
dotnet test                                               # all tests
dotnet test tests/AIHelperNET.Domain.Tests                # single project
dotnet test --filter "FullyQualifiedName~QuestionDetector" # single test
```

`Directory.Build.props`: `TreatWarningsAsErrors`, nullable, latest C#, XML docs on all projects.

## Architecture

Clean/Hexagonal + DDD + CQRS (source-gen [Mediator](https://github.com/martinothamar/Mediator) library).

```
Domain        — Session aggregate, value objects, DomainResult<T>, strongly-typed ID wrappers
Application   — CQRS handlers, port interfaces (Abstractions/), TranscriptPipelineService
Infrastructure — EF Core/SQLite, NAudio, Whisper.NET, Claude/Ollama HTTP, Windows APIs
App           — WPF, ViewModels, sink wiring, MS Generic Host
```

**Adding a handler**: implement `ICommandHandler<TCommand>` or `IQueryHandler<TQuery, TResult>` — Mediator's source generator registers it automatically. FluentValidation validators are auto-discovered from the Application assembly.

**Port interfaces** (`Application/Abstractions/`): `ITranscriptSink`, `IAnswerStreamSink`, `IAudioCaptureService`, `ITranscriptionService`, `IAnswerProvider`, `ISessionRepository`, `IUnitOfWork`, `ISettingsStore`, `ISecretStore`.

**Domain operations** never throw — they return `DomainResult<T>`. All entity IDs are strongly-typed `Guid` wrappers (`SessionId`, `ConversationTurnId`, etc.).

## Key non-obvious facts

**Data root**: `D:\AIHelperNET\` when D: is ready, else `%LocalAppData%\AIHelperNET\`. Models, SQLite DB, settings JSON, and logs all live there.

**Sink wiring**: `TranscriptSink` and `AnswerStreamSink` are registered as singletons. Their handlers are set in `App.OnStartup` via `sink.SetHandler(...)` *after* the DI host starts but *before* `overlay.Show()`. The handler uses `Dispatcher.BeginInvoke` to marshal to the UI thread.

**WPF binding**: never bind `ItemsSource` through a non-`INotifyPropertyChanged` intermediate. Use `DataContext="{Binding Transcript}"` on the sub-element so the ItemsControl can bind `{Binding Items}` as a single hop on a proper `ObservableObject`. A two-hop path through a plain class silently drops `CollectionChanged` subscriptions.

**WhisperModelProvider**: guarded by `SemaphoreSlim(1)` only during initial load/download. Once a `WhisperFactory` is cached, two processors built from the same factory can run in parallel (`factory.CreateBuilder().Build()` is independent per call).

**Gitflow**: `feature/*` → `develop` → `release/*` → `master`. Never commit directly to `master`.
