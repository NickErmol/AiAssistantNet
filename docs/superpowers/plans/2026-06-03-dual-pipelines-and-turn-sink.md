# Dual Pipelines + ConversationTurnSink Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix the blank response window (Feature 2) by adding `IConversationTurnSink` so the UI learns about new turns, and split the single audio pipeline into two independent mic/loopback pipelines (Feature 1) so each speaker's VAD runs in isolation.

**Architecture:**
- Feature 2 follows the existing `ITranscriptSink`/`AnswerStreamSink` pattern: a new port interface in `Application/Abstractions/`, a concrete adapter in `App/Streaming/`, registered as a singleton pair, and wired in `App.OnStartup`.
- Feature 1 replaces the single `Channel<AudioFrame>` in `SessionRunner.RunAsync` with two per-speaker channels, two parallel transcription tasks, and a merge `Channel<TranscriptSegment>` that serialises calls to `pipeline.ProcessAsync`.
- Feature 2 must be implemented first because Feature 1's test setup depends on `TranscriptPipelineService`'s updated constructor signature.

**Tech Stack:** .NET 10, C# 13, `System.Threading.Channels`, NSubstitute 5, xUnit 2, FluentAssertions 8.

---

## File Map

| Status | Path | Purpose |
|--------|------|---------|
| Create | `src/AIHelperNET.Application/Abstractions/IConversationTurnSink.cs` | Port interface for new-turn notifications |
| Modify | `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` | Inject + call `IConversationTurnSink` when a turn is created |
| Modify | `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs` | Pass stub sink; add new test |
| Create | `src/AIHelperNET.App/Streaming/ConversationTurnSinkAdapter.cs` | Concrete adapter; marshals to UI thread |
| Modify | `src/AIHelperNET.App/DependencyInjection.cs` | Register adapter as singleton pair |
| Modify | `src/AIHelperNET.App/App.xaml.cs` | Wire adapter → `ConversationTurnViewModel.AddTurn` |
| Modify | `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj` | Add NSubstitute + App reference for SessionRunner tests |
| Create | `tests/AIHelperNET.Integration.Tests/Sessions/SessionRunnerTests.cs` | Regression tests for dual pipeline routing |
| Modify | `src/AIHelperNET.App/Services/SessionRunner.cs` | Replace single channel with two per-speaker channels + merge |

---

## Task 1: IConversationTurnSink interface

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IConversationTurnSink.cs`

- [ ] **Step 1: Create the interface**

```csharp
// src/AIHelperNET.Application/Abstractions/IConversationTurnSink.cs
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for notifying the UI when a new conversation turn is created.</summary>
public interface IConversationTurnSink
{
    /// <summary>Called when a new turn is opened from a detected question.</summary>
    void OnTurnCreated(ConversationTurnId turnId, string question);
}
```

- [ ] **Step 2: Build to confirm it compiles**

```
dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj
```
Expected: no errors.

- [ ] **Step 3: Commit**

```
git add src/AIHelperNET.Application/Abstractions/IConversationTurnSink.cs
git commit -m "feat: add IConversationTurnSink port interface"
```

---

## Task 2: Update TranscriptPipelineService — TDD

Inject `IConversationTurnSink` into `TranscriptPipelineService` and call `OnTurnCreated` whenever a new conversation turn is opened.

**Files:**
- Modify: `tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs`
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`

- [ ] **Step 1: Write the failing test**

Replace the existing `MakeSvc` helper and add a new test. The full updated test file:

```csharp
// tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class TranscriptPipelineServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;

    private static TranscriptItem MakeItem(Speaker speaker, string text)
        => TranscriptItem.Create(speaker, text, Now, 0.9f);

    private static (TranscriptPipelineService svc, IMediator mediator, IConversationTurnSink turnSink)
        MakeSvc(ITranscriptSink sink)
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GenerateAnswerCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        var turnSink = Substitute.For<IConversationTurnSink>();

        return (new TranscriptPipelineService(factory, sink, turnSink), mediator, turnSink);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_CreatesConversationTurn()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, mediator, _) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().HaveCount(1);
        session.ConversationTurns[0].Status.Should().Be(ConversationTurnStatus.Detected);

        await Task.Delay(200);
        await mediator.Received(1).Send(
            Arg.Is<GenerateAnswerCommand>(c => c.TurnId == session.ConversationTurns[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_NoTurnCreated()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        session.ConversationTurns.Should().BeEmpty();
    }

    [Fact]
    public async Task MeSpeakerQuestion_WithPreliminaryReadyTurn_TransitionsToAwaitingClarification()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _) = MakeSvc(transcriptSink);

        var q = DetectedQuestion.Create("Original Q?", QuestionSource.Audio, Now);
        session.AddDetectedQuestion(q);
        var turn = session.AddConversationTurn(q.Id, "Original Q?", Now).Value;
        turn.TransitionTo(ConversationTurnStatus.PreliminaryReady);

        var clarification = MakeItem(Speaker.Me, "Should it cover all error types?");
        await svc.ProcessAsync(session, clarification, CancellationToken.None);

        turn.Status.Should().Be(ConversationTurnStatus.AwaitingClarification);
        turn.ClarificationQuestionIds.Should().HaveCount(1);
    }

    [Fact]
    public async Task TranscriptSink_CalledForEveryItem()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, _) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Me, "Hello");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        transcriptSink.Received(1).OnTranscriptItem(item);
    }

    [Fact]
    public async Task OtherSpeakerQuestion_NoActiveTurn_NotifiesConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "How do you handle dependency injection?");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        var expectedId = session.ConversationTurns[0].Id;
        turnSink.Received(1).OnTurnCreated(expectedId, item.Text);
    }

    [Fact]
    public async Task OtherSpeakerNonQuestion_NoActiveTurn_DoesNotNotifyConversationTurnSink()
    {
        var session = MakeSession();
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var (svc, _, turnSink) = MakeSvc(transcriptSink);

        var item = MakeItem(Speaker.Other, "Great, thanks.");
        await svc.ProcessAsync(session, item, CancellationToken.None);

        turnSink.DidNotReceive().OnTurnCreated(Arg.Any<ConversationTurnId>(), Arg.Any<string>());
    }
}
```

- [ ] **Step 2: Run the test to confirm it fails**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"
```
Expected: compile error — `TranscriptPipelineService` does not accept a third constructor argument.

- [ ] **Step 3: Update TranscriptPipelineService**

Replace the entire file content of `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers.Commands;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace AIHelperNET.Application.Sessions;

/// <summary>Processes incoming transcript items and drives conversation turn lifecycle.</summary>
public sealed class TranscriptPipelineService(
    IServiceScopeFactory scopeFactory,
    ITranscriptSink transcriptSink,
    IConversationTurnSink turnSink)
{
    private readonly QuestionDetector _detector = new();

    /// <summary>Processes a single transcript item against the active session.</summary>
    public Task ProcessAsync(Session session, TranscriptItem item, CancellationToken ct)
    {
        session.AddTranscriptItem(item);
        transcriptSink.OnTranscriptItem(item);

        var activeTurn = session.ActiveTurn;
        var recentTexts = session.Questions.Select(q => q.Text).ToList();

        if (item.Speaker == Speaker.Other)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (!detection.IsQuestion) return Task.CompletedTask;

            if (activeTurn is null)
            {
                var q = DetectedQuestion.Create(item.Text, QuestionSource.Audio, item.Timestamp);
                session.AddDetectedQuestion(q);
                var turnResult = session.AddConversationTurn(q.Id, item.Text, item.Timestamp);
                if (turnResult.IsFailed) return Task.CompletedTask;
                var turn = turnResult.Value;

                turnSink.OnTurnCreated(turn.Id, item.Text);
                FireAndForget(new GenerateAnswerCommand(session.Id, turn.Id, AnswerVersionType.Preliminary), ct);
            }
            else if (activeTurn.Status == ConversationTurnStatus.AwaitingClarification)
            {
                activeTurn.AttachClarificationResponse(item.Id);
                activeTurn.TransitionTo(ConversationTurnStatus.ClarificationReceived);

                FireAndForget(new GenerateAnswerCommand(
                    session.Id, activeTurn.Id, AnswerVersionType.RefinedAfterClarification), ct);
            }
        }
        else if (item.Speaker == Speaker.Me &&
                 activeTurn?.Status == ConversationTurnStatus.PreliminaryReady)
        {
            var detection = _detector.Evaluate(item.Text, recentTexts);
            if (!detection.IsQuestion) return Task.CompletedTask;

            activeTurn.AttachClarificationQuestion(item.Id);
            activeTurn.TransitionTo(ConversationTurnStatus.AwaitingClarification);
        }

        return Task.CompletedTask;
    }

    private void FireAndForget(GenerateAnswerCommand command, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            using var scope = scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            await mediator.Send(command, ct);
        }, ct);
    }
}
```

- [ ] **Step 4: Run the tests to confirm they pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~TranscriptPipelineServiceTests"
```
Expected: 6 tests, all pass.

- [ ] **Step 5: Build the full solution to catch any other compilation breaks**

```
dotnet build
```
Expected: build errors in `AIHelperNET.App` because `TranscriptPipelineService` is now constructed somewhere with the old 2-arg signature. The DI container (`Application/DependencyInjection.cs`) uses constructor injection — no explicit `new` — so DI is fine. But check if `SessionRunner` or any other file constructs it explicitly.

If there are errors, fix them now. (Likely the only break is DI — the container will auto-resolve `IConversationTurnSink` once it's registered in Task 3.)

- [ ] **Step 6: Commit**

```
git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs
git add tests/AIHelperNET.Application.Tests/Sessions/TranscriptPipelineServiceTests.cs
git commit -m "feat: TranscriptPipelineService calls IConversationTurnSink on turn creation"
```

---

## Task 3: ConversationTurnSinkAdapter + DI wiring

Wire the new sink into the App layer so `ConversationTurnViewModel.AddTurn` is called on the UI thread whenever a new turn is created.

**Files:**
- Create: `src/AIHelperNET.App/Streaming/ConversationTurnSinkAdapter.cs`
- Modify: `src/AIHelperNET.App/DependencyInjection.cs`
- Modify: `src/AIHelperNET.App/App.xaml.cs`

- [ ] **Step 1: Create ConversationTurnSinkAdapter**

```csharp
// src/AIHelperNET.App/Streaming/ConversationTurnSinkAdapter.cs
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;

namespace AIHelperNET.App.Streaming;

/// <summary>Dispatches new-turn notifications to the UI thread.</summary>
public sealed class ConversationTurnSinkAdapter : IConversationTurnSink
{
    private Action<ConversationTurnId, string>? _handler;

    /// <summary>Registers the UI-thread callback.</summary>
    public void SetHandler(Action<ConversationTurnId, string> handler) => _handler = handler;

    /// <inheritdoc/>
    public void OnTurnCreated(ConversationTurnId turnId, string question)
    {
        if (_handler is not null)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                () => _handler(turnId, question));
    }
}
```

- [ ] **Step 2: Register ConversationTurnSinkAdapter in DependencyInjection.cs**

In `src/AIHelperNET.App/DependencyInjection.cs`, add two lines inside `AddPresentation()`, directly after the `TranscriptSink` registration:

Current block (lines 17–21):
```csharp
        // Sinks — singleton so the same instance is used by both infrastructure and ViewModels
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());
        services.AddSingleton<TranscriptSink>();
        services.AddSingleton<ITranscriptSink>(sp => sp.GetRequiredService<TranscriptSink>());
```

Replace with:
```csharp
        // Sinks — singleton so the same instance is used by both infrastructure and ViewModels
        services.AddSingleton<AnswerStreamSink>();
        services.AddSingleton<IAnswerStreamSink>(sp => sp.GetRequiredService<AnswerStreamSink>());
        services.AddSingleton<TranscriptSink>();
        services.AddSingleton<ITranscriptSink>(sp => sp.GetRequiredService<TranscriptSink>());
        services.AddSingleton<ConversationTurnSinkAdapter>();
        services.AddSingleton<IConversationTurnSink>(sp => sp.GetRequiredService<ConversationTurnSinkAdapter>());
```

- [ ] **Step 3: Wire adapter in App.OnStartup**

In `src/AIHelperNET.App/App.xaml.cs`, add the following block directly after the `AnswerStreamSink` wiring block (after line 52, before `overlay.Show()`):

Current block (lines 47–52):
```csharp
        // Wire AnswerStreamSink → ConversationTurnViewModel
        var answerSink = _host.Services.GetRequiredService<AnswerStreamSink>();
        var turnVm     = _host.Services.GetRequiredService<ConversationTurnViewModel>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => ConversationTurnViewModel.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));
```

Replace with:
```csharp
        // Wire AnswerStreamSink → ConversationTurnViewModel
        var answerSink = _host.Services.GetRequiredService<AnswerStreamSink>();
        var turnVm     = _host.Services.GetRequiredService<ConversationTurnViewModel>();
        answerSink.SetHandlers(
            onChunk:    (id, type, chunk) => turnVm.OnChunk(id, type, chunk),
            onComplete: (id, type)        => ConversationTurnViewModel.OnComplete(id, type),
            onError:    (id, err)         => turnVm.OnError(id, err));

        // Wire ConversationTurnSinkAdapter → ConversationTurnViewModel
        var turnCreatedSink = _host.Services.GetRequiredService<ConversationTurnSinkAdapter>();
        turnCreatedSink.SetHandler((id, question) => turnVm.AddTurn(id, question));
```

- [ ] **Step 4: Build and run all tests**

```
dotnet build
dotnet test tests/AIHelperNET.Application.Tests
```
Expected: build succeeds (DI container now resolves `IConversationTurnSink`), all tests pass.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.App/Streaming/ConversationTurnSinkAdapter.cs
git add src/AIHelperNET.App/DependencyInjection.cs
git add src/AIHelperNET.App/App.xaml.cs
git commit -m "feat: wire ConversationTurnSinkAdapter so response window receives new turns"
```

---

## Task 4: Dual pipeline — test infrastructure

Extend the integration test project to be able to reference `AIHelperNET.App` and write tests for `SessionRunner`.

**Files:**
- Modify: `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
- Create: `tests/AIHelperNET.Integration.Tests/Sessions/SessionRunnerTests.cs`

- [ ] **Step 1: Update the integration test csproj**

Replace the full content of `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows10.0.17763.0</TargetFramework>
    <UseWPF>true</UseWPF>
    <IsPackable>false</IsPackable>
    <!-- Test projects: suppress XML-doc requirement, underscore naming, and disposable-field warning -->
    <NoWarn>$(NoWarn);CS1591;CA1707;CA1001</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FluentAssertions" Version="8.10.0" />
    <PackageReference Include="FluentResults" Version="4.0.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="NetArchTest.Rules" Version="1.3.2" />
    <PackageReference Include="NSubstitute" Version="5.3.0" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="coverlet.collector" Version="6.0.4">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\AIHelperNET.App\AIHelperNET.App.csproj" />
    <ProjectReference Include="..\..\src\AIHelperNET.Application\AIHelperNET.Application.csproj" />
    <ProjectReference Include="..\..\src\AIHelperNET.Domain\AIHelperNET.Domain.csproj" />
    <ProjectReference Include="..\..\src\AIHelperNET.Infrastructure\AIHelperNET.Infrastructure.csproj" />
  </ItemGroup>
</Project>
```

Note: `<UseWPF>true</UseWPF>` is required because `AIHelperNET.App` is a WPF project. Our tests won't instantiate any WPF windows.

- [ ] **Step 2: Write the SessionRunner tests file**

```csharp
// tests/AIHelperNET.Integration.Tests/Sessions/SessionRunnerTests.cs
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Integration.Tests.Sessions;

public class SessionRunnerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private static Session MakeSession()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, Now).Value;

    private static IServiceScopeFactory MakeScopeFactory(Session session)
    {
        var mediator = Substitute.For<IMediator>();

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(IMediator)).Returns(mediator);

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.Returns(provider);

        var repo = Substitute.For<ISessionRepository>();
        repo.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(session)));
        provider.GetService(typeof(ISessionRepository)).Returns(repo);

        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);

        return factory;
    }

    private static SessionRunner MakeRunner(
        Session session,
        IAsyncEnumerable<AudioFrame> captureFrames,
        bool useFakeTranscription = true)
    {
        var scopeFactory = MakeScopeFactory(session);
        var transcriptSink = Substitute.For<ITranscriptSink>();
        var turnSink = Substitute.For<IConversationTurnSink>();
        var pipeline = new TranscriptPipelineService(scopeFactory, transcriptSink, turnSink);
        var capture = new FakeAudioCaptureService(captureFrames);
        var transcription = new FakeTranscriptionService();
        return new SessionRunner(scopeFactory, capture, transcription, pipeline);
    }

    [Fact]
    public async Task BothMode_MicAndLoopbackFrames_BothEndInTranscript()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames.ToAsyncEnumerable());

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.Both);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Other);
    }

    [Fact]
    public async Task MicrophoneOnlyMode_LoopbackFrameDropped()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames.ToAsyncEnumerable());

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.MicrophoneOnly);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().NotContain(i => i.Speaker == Speaker.Other);
    }

    [Fact]
    public async Task SystemAudioOnlyMode_MicFrameDropped()
    {
        var session = MakeSession();
        var frames = new[]
        {
            new AudioFrame([0.5f], Speaker.Me,    Now),
            new AudioFrame([0.5f], Speaker.Other, Now),
        };
        var runner = MakeRunner(session, frames.ToAsyncEnumerable());

        await runner.StartAsync(session.Id, new AudioDeviceSelection(null, null),
            WhisperModelSize.Base, AudioSourceMode.SystemAudioOnly);
        await Task.Delay(500);
        await runner.StopAsync();

        session.Transcript.Should().NotContain(i => i.Speaker == Speaker.Me);
        session.Transcript.Should().Contain(i => i.Speaker == Speaker.Other);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private sealed class FakeAudioCaptureService(IAsyncEnumerable<AudioFrame> source)
        : IAudioCaptureService
    {
        public async IAsyncEnumerable<AudioFrame> CaptureAsync(
            AudioDeviceSelection selection,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var frame in source.WithCancellation(ct))
            {
                ct.ThrowIfCancellationRequested();
                yield return frame;
                await Task.Yield();
            }
        }
    }

    /// <summary>
    /// Passes each AudioFrame through as a TranscriptSegment with no VAD or Whisper.
    /// Allows verifying speaker routing without real audio processing.
    /// </summary>
    private sealed class FakeTranscriptionService : ITranscriptionService
    {
        public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
            IAsyncEnumerable<AudioFrame> frames,
            WhisperModelSize model,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (var frame in frames.WithCancellation(ct))
            {
                yield return new TranscriptSegment(
                    $"text-{frame.Speaker}", frame.Speaker, frame.CapturedAt, 1.0f);
            }
        }
    }
}

// Helper: converts IEnumerable<T> to IAsyncEnumerable<T>
file static class EnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
```

- [ ] **Step 3: Run the tests to confirm they compile and establish a baseline**

```
dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~SessionRunnerTests"
```

Expected with the CURRENT single-channel implementation:
- `BothMode_MicAndLoopbackFrames_BothEndInTranscript` — **PASS** (both frames go through single pipeline)
- `MicrophoneOnlyMode_LoopbackFrameDropped` — **PASS** (FilterFrames already handles this)
- `SystemAudioOnlyMode_MicFrameDropped` — **PASS** (FilterFrames already handles this)

All three pass now. They serve as regression tests: if the dual-pipeline refactor breaks speaker routing, they will catch it.

- [ ] **Step 4: Commit test infrastructure before touching production code**

```
git add tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj
git add tests/AIHelperNET.Integration.Tests/Sessions/SessionRunnerTests.cs
git commit -m "test: add SessionRunner regression tests for speaker routing"
```

---

## Task 5: Dual pipeline — implement in SessionRunner

Replace the single `Channel<AudioFrame>` with two per-speaker channels. Two parallel transcription tasks write into a merge `Channel<TranscriptSegment>`, which is consumed sequentially to keep `pipeline.ProcessAsync` single-threaded.

**Files:**
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`

- [ ] **Step 1: Replace the RunAsync method**

Replace the entire file content of `src/AIHelperNET.App/Services/SessionRunner.cs`:

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace AIHelperNET.App.Services;

/// <summary>Drives the audio → transcription → pipeline loop for an active session.</summary>
public sealed class SessionRunner(
    IServiceScopeFactory scopeFactory,
    IAudioCaptureService audioCapture,
    ITranscriptionService transcription,
    TranscriptPipelineService pipeline)
{
    private CancellationTokenSource? _cts;
    private Task? _pipelineTask;
    private IServiceScope? _sessionScope;

    /// <summary>Loads the session and starts the audio pipeline in the background.</summary>
    public async Task StartAsync(
        SessionId sessionId,
        AudioDeviceSelection devices,
        WhisperModelSize model,
        AudioSourceMode audioSource)
    {
        _sessionScope = scopeFactory.CreateScope();
        var repo = _sessionScope.ServiceProvider.GetRequiredService<ISessionRepository>();

        var result = await repo.GetAsync(sessionId, CancellationToken.None);
        if (result.IsFailed)
        {
            Log.Warning("SessionRunner: failed to load session {Id} — {Errors}", sessionId, result.Errors);
            _sessionScope.Dispose();
            _sessionScope = null;
            return;
        }

        _cts          = new CancellationTokenSource();
        _pipelineTask = RunAsync(result.Value, devices, model, audioSource, _cts.Token);
    }

    /// <summary>Stops audio capture and waits for the pipeline to drain.</summary>
    public async Task StopAsync()
    {
        if (_cts is null) return;
        await _cts.CancelAsync();
        if (_pipelineTask is not null)
            await _pipelineTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        _sessionScope?.Dispose();
        _sessionScope = null;
        _cts          = null;
        _pipelineTask = null;
    }

    private async Task RunAsync(
        Session session,
        AudioDeviceSelection devices,
        WhisperModelSize model,
        AudioSourceMode audioSource,
        CancellationToken ct)
    {
        Log.Information("SessionRunner: pipeline starting (mode={AudioSource})", audioSource);

        bool runMic      = audioSource != AudioSourceMode.SystemAudioOnly;
        bool runLoopback = audioSource != AudioSourceMode.MicrophoneOnly;

        // Two per-speaker channels feed two independent VAD+Whisper instances.
        var micChannel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var loopbackChannel = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // Merge channel serialises calls to pipeline.ProcessAsync — Session is not thread-safe.
        var mergeChannel = Channel.CreateUnbounded<TranscriptSegment>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        // Capture task: fan out NAudio frames to the appropriate per-speaker channel.
        var captureTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in audioCapture.CaptureAsync(devices, ct))
                {
                    if (frame.Speaker == Speaker.Me && runMic)
                        await micChannel.Writer.WriteAsync(frame, ct);
                    else if (frame.Speaker == Speaker.Other && runLoopback)
                        await loopbackChannel.Writer.WriteAsync(frame, ct);
                }
            }
            catch (OperationCanceledException) { }
            finally
            {
                micChannel.Writer.TryComplete();
                loopbackChannel.Writer.TryComplete();
                Log.Information("SessionRunner: capture stopped");
            }
        }, ct);

        // Mic transcription task (Speaker.Me — interviewer follow-ups).
        var micTask = runMic
            ? Task.Run(async () =>
            {
                try
                {
                    await foreach (var seg in transcription
                        .TranscribeAsync(micChannel.Reader.ReadAllAsync(ct), model, ct)
                        .WithCancellation(ct))
                    {
                        await mergeChannel.Writer.WriteAsync(seg, ct);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct)
            : Task.CompletedTask;

        // Loopback transcription task (Speaker.Other — interviewer questions).
        var loopbackTask = runLoopback
            ? Task.Run(async () =>
            {
                try
                {
                    await foreach (var seg in transcription
                        .TranscribeAsync(loopbackChannel.Reader.ReadAllAsync(ct), model, ct)
                        .WithCancellation(ct))
                    {
                        await mergeChannel.Writer.WriteAsync(seg, ct);
                    }
                }
                catch (OperationCanceledException) { }
            }, ct)
            : Task.CompletedTask;

        // Complete the merge channel once both transcription tasks finish.
        _ = Task.WhenAll(micTask, loopbackTask)
            .ContinueWith(_ => mergeChannel.Writer.TryComplete(), TaskScheduler.Default);

        // Sequential consumer: pipeline.ProcessAsync must not be called concurrently.
        try
        {
            await foreach (var seg in mergeChannel.Reader.ReadAllAsync(ct))
            {
                Log.Information("SessionRunner: segment [{Speaker}] conf={Conf:F2} — {Text}",
                    seg.Speaker, seg.Confidence, seg.Text);
                var item = TranscriptItem.Create(seg.Speaker, seg.Text, seg.CapturedAt, seg.Confidence);
                await pipeline.ProcessAsync(session, item, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error(ex, "SessionRunner: unhandled error in pipeline");
        }

        await captureTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        await Task.WhenAll(micTask, loopbackTask).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Log.Information("SessionRunner: pipeline stopped");
    }
}
```

Key changes vs old code:
- Removed `FilterFrames` method (its logic is now inlined in the capture fan-out)
- Two `Channel<AudioFrame>`: `micChannel` and `loopbackChannel`
- Two transcription tasks writing to `mergeChannel`
- `Task.WhenAll(micTask, loopbackTask).ContinueWith(...)` closes the merge channel
- Sequential consumer reads from `mergeChannel`

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
```
Expected: no errors.

- [ ] **Step 3: Run the regression tests**

```
dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~SessionRunnerTests"
```
Expected: all 3 tests pass.

- [ ] **Step 4: Run the full test suite**

```
dotnet test
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add src/AIHelperNET.App/Services/SessionRunner.cs
git commit -m "feat: split audio pipeline into parallel mic/loopback channels with merge"
```

---

## Self-Review Checklist

### Spec coverage

| Requirement | Task |
|---|---|
| `IConversationTurnSink` interface in `Application/Abstractions/` | Task 1 |
| `TranscriptPipelineService` calls sink after `AddConversationTurn` | Task 2 |
| `ConversationTurnSinkAdapter` in `App/Streaming/` with `BeginInvoke` | Task 3 |
| Register as singleton pair in App DI | Task 3 |
| Wire in `App.OnStartup` → `ConversationTurnViewModel.AddTurn` | Task 3 |
| Two separate channels: micChannel, loopbackChannel | Task 5 |
| Two parallel `Task.Run` transcription tasks | Task 5 |
| Merge channel serialises `pipeline.ProcessAsync` | Task 5 |
| `AudioSourceMode` routing (MicOnly / LoopbackOnly / Both) | Task 5 |
| `WhisperModelProvider` safety note (semaphore only on initial load) | Inherent — no change needed |

### No placeholders: confirmed — all steps have complete code.

### Type consistency:
- `ConversationTurnId` — same type used in interface, adapter, and ViewModel
- `IConversationTurnSink.OnTurnCreated(ConversationTurnId, string)` — matches adapter's `SetHandler(Action<ConversationTurnId, string>)`
- `TranscriptPipelineService(IServiceScopeFactory, ITranscriptSink, IConversationTurnSink)` — 3-arg ctor used consistently in tests and DI
- `SessionRunner` ctor signature unchanged — DI resolves all 4 deps as before
