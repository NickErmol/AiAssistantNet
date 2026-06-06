# Code Review Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix 10 bugs and cleanup issues identified in the develop-branch code review, covering UI correctness, state machine ordering, resource leaks, and hot-path efficiency.

**Architecture:** All changes are surgical edits to existing files. No new abstractions, no new projects. Tests are added to existing test projects using the established NSubstitute + FluentAssertions + xUnit pattern.

**Tech Stack:** .NET 10, C# 13, CommunityToolkit.Mvvm (`ObservableObject`, `SetProperty`), FluentResults, NSubstitute, xUnit, FluentAssertions, NAudio, WPF

---

## File Map

| File | Change |
|------|--------|
| `src/AIHelperNET.App/ViewModels/HistoryViewModel.cs` | `SessionSummaryVm` → extend `ObservableObject`; remove redundant `OnPropertyChanged(nameof(Sessions))` |
| `src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs` | Move `TransitionTo` after `StartAnswer` guard |
| `src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerWithScreenCommand.cs` | Move `TransitionTo` after `StartAnswer` guard |
| `src/AIHelperNET.App/Services/SessionRunner.cs` | Add 5-second timeout in `StopAsync`; handle `ChannelClosedException` in consumer |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` | `using var secure = new SecureString()` |
| `src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs` | Dispose `_cts`; capture `WaveFormat` locally before event registration |
| `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` | Log exceptions from fire-and-forget `ChangeModeAsync` calls |
| `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` | Extract `CreateNewVersion` helper to remove duplicated version-reset pattern |
| `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs` | Remove `.ToList()` on hot path |
| `src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs` | Wrap `BitmapDecoder` in `using` |
| `tests/AIHelperNET.Application.Tests/Answers/GenerateFollowUpHandlerTests.cs` | New: tests for state machine ordering fix |

---

## Task 1 — Fix `SessionSummaryVm`: expand/collapse completely broken

**Problem:** `SessionSummaryVm.IsExpanded` fires a custom `Action` event that WPF never subscribes to. The XAML `{Binding IsExpanded, Converter=BoolToVisibilityConverter}` never updates. The expand/collapse feature is silently non-functional.

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/HistoryViewModel.cs`

- [ ] **Step 1: Add `ObservableObject` base class and use `SetProperty`**

  Replace the `SessionSummaryVm` class definition (lines 13–31):

  ```csharp
  public sealed class SessionSummaryVm(SessionSummaryDto dto) : ObservableObject
  {
      public SessionId         Id            => dto.Id;
      public string            DateLabel     => dto.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
      public string            Mode          => dto.State.ToString();
      public int               QuestionCount => dto.QuestionCount;
      public int               AnswerCount   => dto.AnswerCount;
      public bool              IsActive      => dto.EndedAt is null;

      private bool _isExpanded;
      public bool IsExpanded
      {
          get => _isExpanded;
          set => SetProperty(ref _isExpanded, value);
      }

      public SessionDetailDto? Detail { get; set; }
  }
  ```

  `ObservableObject` is from `CommunityToolkit.Mvvm.ComponentModel` — already referenced by the App project via `ConversationTurnViewModel`. No new package needed.

- [ ] **Step 2: Remove the now-redundant `OnPropertyChanged(nameof(Sessions))` call**

  In `ToggleExpandAsync`, remove line 67:

  ```csharp
  [RelayCommand]
  public async Task ToggleExpandAsync(SessionSummaryVm? vm)
  {
      if (vm is null) return;
      if (!vm.IsExpanded)
      {
          if (vm.Detail is null)
          {
              var result = await mediator.Send(new GetSessionDetailQuery(vm.Id));
              if (result.IsSuccess) vm.Detail = result.Value;
          }
          vm.IsExpanded = true;
      }
      else
      {
          vm.IsExpanded = false;
      }
  }
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Manual verification**

  Run the app, open the History panel (if sessions exist), click a session row — the transcript/answer detail panel should appear. Click again — it should collapse.

- [ ] **Step 5: Commit**

  ```
  git add src/AIHelperNET.App/ViewModels/HistoryViewModel.cs
  git commit -m "fix: make SessionSummaryVm extend ObservableObject so IsExpanded binding updates in WPF"
  ```

---

## Task 2 — Fix state machine: turn stranded in `GeneratingRefined` when `StartAnswer` fails

**Problem:** Both `GenerateFollowUpHandler` and `RegenerateAnswerWithScreenHandler` call `turn.TransitionTo(GeneratingRefined)` **before** validating the `session.StartAnswer(...)` result. If `StartAnswer` returns a failure (e.g. duplicate answer in progress), the handler returns early leaving the in-memory `Session` with a `GeneratingRefined` turn and no answer object — subsequent operations on that session scope see inconsistent state.

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs`
- Modify: `src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerWithScreenCommand.cs`
- Create: `tests/AIHelperNET.Application.Tests/Answers/GenerateFollowUpHandlerTests.cs`

- [ ] **Step 1: Write the failing test**

  Create `tests/AIHelperNET.Application.Tests/Answers/GenerateFollowUpHandlerTests.cs`:

  ```csharp
  using AIHelperNET.Application.Abstractions;
  using AIHelperNET.Application.Answers.Commands;
  using AIHelperNET.Domain.Ids;
  using AIHelperNET.Domain.Sessions;
  using AIHelperNET.Domain.ValueObjects;
  using FluentAssertions;
  using FluentResults;
  using Microsoft.Extensions.Time.Testing;
  using NSubstitute;
  using Xunit;

  namespace AIHelperNET.Application.Tests.Answers;

  public class GenerateFollowUpHandlerTests
  {
      [Fact]
      public async Task Handle_StartAnswerFails_ReturnsFailureWithoutTransitioningTurn()
      {
          // Arrange
          var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty,
              SessionMode.AudioOnly, AudioSourceMode.Both).Value;

          // Add a question and turn so the handler can find them
          var q = DetectedQuestion.Create("What is X?", QuestionSource.Audio, DateTimeOffset.UtcNow);
          session.AddDetectedQuestion(q);
          var turnResult = session.AddConversationTurn(q.Id, q.Text, DateTimeOffset.UtcNow);
          var turn = turnResult.Value;

          // Put turn in Answered state so StartAnswer will fail (already answered)
          // StartAnswer succeeds only from specific states — force a state where it fails.
          // We'll start an answer then complete it so a second StartAnswer on same question fails.
          var firstStart = session.StartAnswer(q.Id, DateTimeOffset.UtcNow);
          firstStart.IsSuccess.Should().BeTrue();
          firstStart.Value.Complete(DateTimeOffset.UtcNow);

          var repo = Substitute.For<ISessionRepository>();
          repo.GetAsync(session.Id, Arg.Any<CancellationToken>())
              .Returns(Result.Ok(session));

          var uow             = Substitute.For<IUnitOfWork>();
          var settingsStore   = Substitute.For<ISettingsStore>();
          var providerResolver = Substitute.For<IAnswerProviderResolver>();
          var streamSink      = Substitute.For<IAnswerStreamSink>();
          var clock           = new FakeTimeProvider(DateTimeOffset.UtcNow);

          settingsStore.LoadAsync(Arg.Any<CancellationToken>())
              .Returns(AppSettings.Default);

          var handler = new GenerateFollowUpHandler(
              repo, providerResolver, settingsStore, streamSink, uow, clock);

          var statusBefore = turn.Status;

          // Act
          var result = await handler.Handle(
              new GenerateFollowUpCommand(session.Id, turn.Id, "follow-up text"),
              CancellationToken.None);

          // Assert
          result.IsFailed.Should().BeTrue();
          turn.Status.Should().Be(statusBefore, "turn must not transition when StartAnswer fails");
      }
  }
  ```

  > **Note:** `AppSettings.Default` may not exist — check `AppSettingsDto` or `SettingsStore` for how to construct a default settings object; substitute the correct type/factory. Similarly, verify `Session.Create` signature matches the current codebase.

- [ ] **Step 2: Run test to confirm it fails**

  ```
  dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateFollowUpHandlerTests" -v
  ```

  Expected: the test may fail compilation or assertion — either is acceptable at this stage.

- [ ] **Step 3: Fix `GenerateFollowUpCommand.cs` — move `TransitionTo` after the guard**

  Current lines 47–51:
  ```csharp
  turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

  var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
  if (start.IsFailed) return Result.Fail(start.Error);
  var answer = start.Value;
  ```

  Replace with:
  ```csharp
  var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
  if (start.IsFailed) return Result.Fail(start.Error);
  var answer = start.Value;

  turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);
  ```

- [ ] **Step 4: Fix `RegenerateAnswerWithScreenCommand.cs` — same reorder**

  Current lines 44–48:
  ```csharp
  turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);

  var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
  if (start.IsFailed) return Result.Fail(start.Error);
  var answer = start.Value;
  ```

  Replace with:
  ```csharp
  var start = session.StartAnswer(turn.InitialQuestionId, clock.GetUtcNow());
  if (start.IsFailed) return Result.Fail(start.Error);
  var answer = start.Value;

  turn.TransitionTo(ConversationTurnStatus.GeneratingRefined);
  ```

- [ ] **Step 5: Run the test again — confirm it passes**

  ```
  dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~GenerateFollowUpHandlerTests" -v
  ```

  Expected: PASS.

- [ ] **Step 6: Run full test suite**

  ```
  dotnet test
  ```

  Expected: all tests pass (0 failures).

- [ ] **Step 7: Commit**

  ```
  git add src/AIHelperNET.Application/Answers/Commands/GenerateFollowUpCommand.cs
  git add src/AIHelperNET.Application/Answers/Commands/RegenerateAnswerWithScreenCommand.cs
  git add tests/AIHelperNET.Application.Tests/Answers/GenerateFollowUpHandlerTests.cs
  git commit -m "fix: validate StartAnswer result before transitioning turn to GeneratingRefined"
  ```

---

## Task 3 — Fix `SessionRunner.StopAsync`: add timeout; handle `ChannelClosedException` cleanly

**Problem A:** `StopAsync` awaits `_pipelineTask` with no timeout. If `WhisperTranscriptionService` has an in-flight network or model call when cancellation fires, the transcription tasks never complete, `mergeChannel` is never closed, and `StopAsync` blocks indefinitely.

**Problem B:** In the consumer task, when `pending != null` and the merge channel is completed during the 300 ms window, `mergeChannel.Reader.ReadAsync(window.Token)` throws `ChannelClosedException` (a subclass of `InvalidOperationException`, not `OperationCanceledException`). This lands in the outer `catch (Exception ex) when (ex is not OperationCanceledException)` logger at line 206, printing a spurious error before the `finally` drains normally.

**Files:**
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`

- [ ] **Step 1: Add a 5-second shutdown timeout in `StopAsync`**

  Current `StopAsync` (lines 47–57):
  ```csharp
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
  ```

  Replace with:
  ```csharp
  public async Task StopAsync()
  {
      if (_cts is null) return;
      await _cts.CancelAsync();
      if (_pipelineTask is not null)
      {
          using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
          try
          {
              await _pipelineTask.WaitAsync(timeout.Token)
                  .ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
          }
          catch (OperationCanceledException)
          {
              Log.Warning("SessionRunner: pipeline did not stop within 5 s; abandoning");
          }
      }
      _sessionScope?.Dispose();
      _sessionScope = null;
      _cts          = null;
      _pipelineTask = null;
  }
  ```

- [ ] **Step 2: Handle `ChannelClosedException` in the consumer merge-window catch**

  In the `consumerTask` lambda, the inner `try/catch` block currently only catches `OperationCanceledException` (around line 178–186). Extend it to also catch `ChannelClosedException` so shutdown doesn't hit the error logger:

  ```csharp
  try
  {
      next = await mergeChannel.Reader.ReadAsync(window.Token);
  }
  catch (OperationCanceledException)
  {
      await FlushAsync(); // timeout — no follow-on arrived
      continue;
  }
  catch (System.Threading.Channels.ChannelClosedException)
  {
      await FlushAsync(); // channel closed during merge window — drain and exit
      break;
  }
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run full test suite**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```
  git add src/AIHelperNET.App/Services/SessionRunner.cs
  git commit -m "fix: add 5-second StopAsync timeout and handle ChannelClosedException in consumer merge window"
  ```

---

## Task 4 — Fix small resource/security leaks: `SecureString` disposal and `CancellationTokenSource` disposal

Both are one-liner fixes with no test required beyond a passing build.

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs`

- [ ] **Step 1: Fix `SecureString` — add `using`**

  In `SettingsViewModel.cs`, `SaveApiKeyAsync` method (around line 103):

  Current:
  ```csharp
  var secure = new System.Security.SecureString();
  foreach (var c in ApiKeyInput) secure.AppendChar(c);
  secure.MakeReadOnly();
  var result = await mediator.Send(new SaveApiKeyCommand(secure));
  ```

  Replace with:
  ```csharp
  using var secure = new System.Security.SecureString();
  foreach (var c in ApiKeyInput) secure.AppendChar(c);
  secure.MakeReadOnly();
  var result = await mediator.Send(new SaveApiKeyCommand(secure));
  ```

  This ensures the unmanaged buffer is zeroed immediately after the command completes.

- [ ] **Step 2: Fix `CancellationTokenSource` leak in `AudioLevelMonitor`**

  In `AudioLevelMonitor.cs`, `StopAsync` method (around line 71). Add `_cts?.Dispose()` and null it:

  Current:
  ```csharp
  public Task StopAsync()
  {
      _cts?.Cancel();
      _mic?.StopRecording();
      _loopback?.StopRecording();
      _mic?.Dispose();
      _loopback?.Dispose();
      _mic      = null;
      _loopback = null;
      Log.Debug("AudioLevelMonitor: stopped");
      return Task.CompletedTask;
  }
  ```

  Replace with:
  ```csharp
  public Task StopAsync()
  {
      _cts?.Cancel();
      _cts?.Dispose();
      _cts = null;
      _mic?.StopRecording();
      _loopback?.StopRecording();
      _mic?.Dispose();
      _loopback?.Dispose();
      _mic      = null;
      _loopback = null;
      Log.Debug("AudioLevelMonitor: stopped");
      return Task.CompletedTask;
  }
  ```

- [ ] **Step 3: Build**

  ```
  dotnet build
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Run full test suite**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```
  git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
  git add src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs
  git commit -m "fix: dispose SecureString after SaveApiKey and dispose CancellationTokenSource in AudioLevelMonitor"
  ```

---

## Task 5 — Fix `AudioLevelMonitor` race: capture `WaveFormat` before event registration

**Problem:** The `DataAvailable` lambda reads `_mic.WaveFormat` through the field reference. `StopAsync` later sets `_mic = null`. There is a window between `StopRecording()` returning and `_mic = null` during which a buffered audio callback could read through the now-null field and throw.

**Fix:** Capture `WaveFormat` into a local variable at startup time. The closure then holds a safe copy with no dependency on the field.

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs`

- [ ] **Step 1: Capture `WaveFormat` into locals before registering the event handlers**

  In `StartAsync`, find the two event-handler registrations. Current (around lines 34–58):

  ```csharp
  _mic = new WasapiCapture(micDevice);
  _mic.DataAvailable += (_, e) =>
  {
      var peak = ComputePeak(e.Buffer, e.BytesRecorded, _mic.WaveFormat);
      MicLevelChanged?.Invoke(peak);
  };
  _mic.StartRecording();
  ```

  and:

  ```csharp
  _loopback = new WasapiLoopbackCapture(loopbackDevice);
  _loopback.DataAvailable += (_, e) =>
  {
      var peak = ComputePeak(e.Buffer, e.BytesRecorded, _loopback.WaveFormat);
      SystemLevelChanged?.Invoke(peak);
  };
  _loopback.StartRecording();
  ```

  Replace with:

  ```csharp
  _mic = new WasapiCapture(micDevice);
  var micFormat = _mic.WaveFormat;        // captured once; closure no longer touches _mic
  _mic.DataAvailable += (_, e) =>
  {
      var peak = ComputePeak(e.Buffer, e.BytesRecorded, micFormat);
      MicLevelChanged?.Invoke(peak);
  };
  _mic.StartRecording();
  ```

  and:

  ```csharp
  _loopback = new WasapiLoopbackCapture(loopbackDevice);
  var loopbackFormat = _loopback.WaveFormat;   // captured once; closure no longer touches _loopback
  _loopback.DataAvailable += (_, e) =>
  {
      var peak = ComputePeak(e.Buffer, e.BytesRecorded, loopbackFormat);
      SystemLevelChanged?.Invoke(peak);
  };
  _loopback.StartRecording();
  ```

- [ ] **Step 2: Build**

  ```
  dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Commit**

  ```
  git add src/AIHelperNET.Infrastructure/Audio/AudioLevelMonitor.cs
  git commit -m "fix: capture WaveFormat locally before event handler registration to eliminate null-field race in AudioLevelMonitor"
  ```

---

## Task 6 — Fix `SessionControlViewModel`: log exceptions from fire-and-forget mode-change calls

**Problem:** `OnModeChanged` and `OnAudioSourceChanged` call `_ = ChangeModeAsync()` (fire-and-forget). If `ChangeModeAsync` throws — e.g., because the mediator send fails — the exception is silently discarded and the session's audio-source mode indicator can become stale.

**Fix:** Attach a fault continuation that logs the exception. This keeps the fire-and-forget pattern (partial void callbacks cannot be made async) but surfaces errors.

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`

- [ ] **Step 1: Attach a logging continuation**

  Current (lines 110–118):
  ```csharp
  partial void OnModeChanged(SessionMode value)
  {
      if (IsSessionActive) _ = ChangeModeAsync();
  }

  partial void OnAudioSourceChanged(AudioSourceMode value)
  {
      if (IsSessionActive) _ = ChangeModeAsync();
  }
  ```

  Replace with:
  ```csharp
  partial void OnModeChanged(SessionMode value)
  {
      if (IsSessionActive)
          _ = ChangeModeAsync().ContinueWith(
              t => Log.Error(t.Exception, "SessionControlViewModel: mode change failed"),
              TaskContinuationOptions.OnlyOnFaulted);
  }

  partial void OnAudioSourceChanged(AudioSourceMode value)
  {
      if (IsSessionActive)
          _ = ChangeModeAsync().ContinueWith(
              t => Log.Error(t.Exception, "SessionControlViewModel: audio-source change failed"),
              TaskContinuationOptions.OnlyOnFaulted);
  }
  ```

  `Serilog.Log` is already imported in the App project (used in `App.xaml.cs`). Verify the `using Serilog;` directive is present at the top of `SessionControlViewModel.cs`; add it if not.

- [ ] **Step 2: Build**

  ```
  dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Step 3: Run full test suite**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 4: Commit**

  ```
  git add src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs
  git commit -m "fix: log exceptions from fire-and-forget ChangeModeAsync in SessionControlViewModel"
  ```

---

## Task 7 — Cleanup: duplicate version-reset, hot-path List allocation, BitmapDecoder disposal

Three independent small cleanups, committed together.

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`
- Modify: `src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs`
- Modify: `src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs`

---

### 7a — Extract `CreateNewVersion` helper in `ConversationTurnViewModel`

**Problem:** `OnChunk` (lines ~147–152) and `OnError` (lines ~167–171) both manually iterate `AnswerVersions` to clear `IsLatest`, then `Insert(0, newVersion)`. The logic is identical except for the version type and text.

- [ ] **Step 1: Extract a private helper method**

  Add this private method inside the `ConversationTurnViewModel` class, below the existing public methods:

  ```csharp
  private static AnswerVersionVm CreateNewVersion(
      TurnVm turn, AnswerVersionId id, AnswerVersionType type, string text = "")
  {
      foreach (var v in turn.AnswerVersions) v.IsLatest = false;
      var version = new AnswerVersionVm(id, type, DateTimeOffset.UtcNow)
          { Text = text, IsLatest = true };
      turn.AnswerVersions.Insert(0, version);
      turn.LatestVersion = version;
      return version;
  }
  ```

- [ ] **Step 2: Simplify `OnChunk` to use the helper**

  Current `OnChunk` body (around lines 139–154):
  ```csharp
  var version = turn.AnswerVersions.FirstOrDefault(v => v.IsLatest);
  if (version is null)
  {
      foreach (var v in turn.AnswerVersions) v.IsLatest = false;
      version = new AnswerVersionVm(AnswerVersionId.New(), versionType, DateTimeOffset.UtcNow)
          { IsLatest = true };
      turn.AnswerVersions.Insert(0, version);
      turn.LatestVersion = version;
  }
  version.Text += chunk;
  ```

  Replace with:
  ```csharp
  var version = turn.AnswerVersions.FirstOrDefault(v => v.IsLatest)
      ?? CreateNewVersion(turn, AnswerVersionId.New(), versionType);
  version.Text += chunk;
  ```

- [ ] **Step 3: Simplify `OnError` to use the helper**

  Current `OnError` body (around lines 163–171):
  ```csharp
  foreach (var v in turn.AnswerVersions) v.IsLatest = false;
  var errVersion = new AnswerVersionVm(AnswerVersionId.New(), AnswerVersionType.Preliminary,
      DateTimeOffset.UtcNow) { Text = $"[Error: {errorMessage}]", IsLatest = true };
  turn.AnswerVersions.Insert(0, errVersion);
  turn.LatestVersion = errVersion;
  ```

  Replace with:
  ```csharp
  CreateNewVersion(turn, AnswerVersionId.New(), AnswerVersionType.Preliminary,
      $"[Error: {errorMessage}]");
  ```

---

### 7b — Remove `.ToList()` on hot path in `TranscriptPipelineService`

**Problem:** Line 29 materialises `session.Questions.Select(q => q.Text).ToList()` on every `ProcessAsync` call (called for every transcribed segment). `QuestionDetector.Evaluate` accepts `IReadOnlyList<string>` or `IEnumerable<string>` — check its actual signature and remove the `.ToList()` if it accepts `IEnumerable`.

- [ ] **Step 1: Check `QuestionDetector.Evaluate` signature**

  Open `src/AIHelperNET.Domain/Questions/QuestionDetector.cs` (or wherever `Evaluate` is defined) and confirm its second parameter type.

- [ ] **Step 2a: If `Evaluate` accepts `IEnumerable<string>` — remove `.ToList()`**

  Current line 29:
  ```csharp
  var recentTexts = session.Questions.Select(q => q.Text).ToList();
  ```

  Replace with:
  ```csharp
  var recentTexts = session.Questions.Select(q => q.Text);
  ```

- [ ] **Step 2b: If `Evaluate` requires `IReadOnlyList<string>` — change parameter to `IReadOnlyList<string>` and keep `.ToList()`**

  If `Evaluate` specifically needs a list (e.g. for random access), leave `.ToList()` in place and skip this sub-task.

---

### 7c — Wrap `BitmapDecoder` in `using` in `WindowsOcrService`

**Problem:** The `BitmapDecoder` returned by `BitmapDecoder.CreateAsync(...)` is not disposed. In the .NET WinRT projection the decoder implements `IDisposable` via the COM object wrapper. Leaving it undisposed delays release of the COM reference.

- [ ] **Step 1: Wrap `decoder` in a `using` statement**

  Current `BitmapToSoftwareBitmapAsync` (lines 35–44):
  ```csharp
  private static async Task<SoftwareBitmap> BitmapToSoftwareBitmapAsync(Bitmap bmp, CancellationToken ct)
  {
      using var ms = new MemoryStream();
      bmp.Save(ms, ImageFormat.Bmp);
      ms.Seek(0, SeekOrigin.Begin);

      var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
      return await decoder.GetSoftwareBitmapAsync()
          .AsTask(ct);
  }
  ```

  Replace with:
  ```csharp
  private static async Task<SoftwareBitmap> BitmapToSoftwareBitmapAsync(Bitmap bmp, CancellationToken ct)
  {
      using var ms = new MemoryStream();
      bmp.Save(ms, ImageFormat.Bmp);
      ms.Seek(0, SeekOrigin.Begin);

      using var decoder = await BitmapDecoder.CreateAsync(ms.AsRandomAccessStream());
      return await decoder.GetSoftwareBitmapAsync()
          .AsTask(ct);
  }
  ```

  > **Note:** If `BitmapDecoder` does not implement `IDisposable` the `using` will cause a compile error — remove it and mark this sub-task N/A.

---

### 7 — Build, test, commit

- [ ] **Build**

  ```
  dotnet build
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Test**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Commit**

  ```
  git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
  git add src/AIHelperNET.Application/Sessions/TranscriptPipelineService.cs
  git add src/AIHelperNET.Infrastructure/Ocr/WindowsOcrService.cs
  git commit -m "refactor: extract CreateNewVersion helper, remove hot-path ToList, dispose BitmapDecoder"
  ```

---

## Final verification

After all tasks are complete:

- [ ] **Run the full test suite one last time**

  ```
  dotnet test --logger "console;verbosity=normal"
  ```

  Expected: all tests green, no warnings promoted to errors.

- [ ] **Build release configuration**

  ```
  dotnet build -c Release
  ```

  Expected: 0 errors, 0 warnings.

- [ ] **Run the app and exercise the fixed flows manually**

  1. Open History panel — expand/collapse a session row (Task 1).
  2. Start and stop a session — confirm it stops within ~5 seconds even if audio is active (Task 3).
  3. Save an API key in Settings (Task 4).
  4. Change audio source mode mid-session — any error should appear in the log file under `D:\AIHelperNET\logs\` (Task 6).
