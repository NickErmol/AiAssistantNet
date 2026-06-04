# Adjustable Answer Font Size Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `−` / `+` buttons to each answer card's action row that resize the answer body text from 9–20 pt, persisted via `ISettingsStore`.

**Architecture:** `AnswerFontSize` (int, default 12) is added to `AppSettingsDto` and exposed as an `[ObservableProperty]` on `ConversationTurnViewModel`. Two `[RelayCommand]` methods send `SaveAnswerFontSizeCommand` on each press. The answer body `TextBlock` binds directly to the ViewModel property instead of `DynamicResource Font.MD`. Startup calls `LoadFontSizeAsync()` on `ConversationTurnViewModel` to restore the saved value before the window shows.

**Tech Stack:** .NET 10, WPF, CommunityToolkit.Mvvm (source-gen RelayCommand/ObservableProperty), Mediator (source-gen CQRS), NSubstitute + xUnit + FluentAssertions for tests.

---

## File Map

| File | Action |
|------|--------|
| `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs` | Modify — add `int AnswerFontSize = 12` |
| `src/AIHelperNET.Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs` | Create — command + handler |
| `tests/AIHelperNET.Application.Tests/Sessions/SaveAnswerFontSizeHandlerTests.cs` | Create — 4 handler tests |
| `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs` | Modify — property, commands, LoadFontSizeAsync |
| `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` | Modify — rebind TextBlock, add buttons |
| `src/AIHelperNET.App/App.xaml.cs` | Modify — call LoadFontSizeAsync before Show() |

---

## Task 1: Add AnswerFontSize to AppSettingsDto

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`

- [ ] **Step 1: Add the new field as the last positional parameter**

Open `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`. The current record is:

```csharp
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId);
```

Replace with:

```csharp
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId,
    int AnswerFontSize = 12);
```

The default value of 12 means:
- `JsonSettingsStore.DefaultSettings()` compiles unchanged (it uses positional args and C# will apply the default for the omitted 7th param).
- Existing JSON settings files that predate this field deserialize with `AnswerFontSize = 12` because `System.Text.Json` on .NET 10 respects record constructor defaults for missing properties.

- [ ] **Step 2: Build to confirm nothing broke**

```
dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs
git commit -m "feat: add AnswerFontSize field to AppSettingsDto"
```

---

## Task 2: Add SaveAnswerFontSizeCommand (TDD)

**Files:**
- Create: `tests/AIHelperNET.Application.Tests/Sessions/SaveAnswerFontSizeHandlerTests.cs`
- Create: `src/AIHelperNET.Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/AIHelperNET.Application.Tests/Sessions/SaveAnswerFontSizeHandlerTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SaveAnswerFontSizeHandlerTests
{
    private static AppSettingsDto MakeSettings(int fontSize = 12) => new(
        AiBackend.Claude,
        WhisperModelSize.Base,
        AnswerSettings.Default,
        CodeProfile.Empty,
        null,
        null,
        fontSize);

    [Fact]
    public async Task Handle_ValueInRange_SavesExactValue()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        var result = await handler.Handle(new SaveAnswerFontSizeCommand(15), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == 15),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValueBelowMin_ClampsToMin()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(1), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == SaveAnswerFontSizeHandler.Min),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValueAboveMax_ClampsToMax()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(99), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == SaveAnswerFontSizeHandler.Max),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PreservesOtherSettingsFields()
    {
        var original = MakeSettings(12);
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(original);

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(16), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s =>
                s.ActiveBackend   == original.ActiveBackend   &&
                s.WhisperModel    == original.WhisperModel    &&
                s.AnswerSettings  == original.AnswerSettings  &&
                s.MicDeviceId     == original.MicDeviceId     &&
                s.LoopbackDeviceId == original.LoopbackDeviceId &&
                s.AnswerFontSize  == 16),
            Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run tests — expect build failure (type not yet defined)**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveAnswerFontSizeHandler"
```

Expected: build error — `SaveAnswerFontSizeCommand` and `SaveAnswerFontSizeHandler` are not defined.

- [ ] **Step 3: Create the command and handler**

Create `src/AIHelperNET.Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Persists a new answer-panel font size to the settings store.</summary>
/// <param name="FontSize">Desired font size; clamped to [<see cref="SaveAnswerFontSizeHandler.Min"/>, <see cref="SaveAnswerFontSizeHandler.Max"/>].</param>
public sealed record SaveAnswerFontSizeCommand(int FontSize) : IRequest<Result>;

/// <summary>Handles <see cref="SaveAnswerFontSizeCommand"/>.</summary>
public sealed class SaveAnswerFontSizeHandler(ISettingsStore settingsStore)
    : IRequestHandler<SaveAnswerFontSizeCommand, Result>
{
    /// <summary>Minimum allowed answer font size (pt).</summary>
    public const int Min = 9;

    /// <summary>Maximum allowed answer font size (pt).</summary>
    public const int Max = 20;

    /// <inheritdoc/>
    public async ValueTask<Result> Handle(SaveAnswerFontSizeCommand command, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(command.FontSize, Min, Max);
        var current = await settingsStore.LoadAsync(cancellationToken);
        await settingsStore.SaveAsync(current with { AnswerFontSize = clamped }, cancellationToken);
        return Result.Ok();
    }
}
```

- [ ] **Step 4: Run tests — expect all 4 to pass**

```
dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveAnswerFontSizeHandler"
```

Expected:
```
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs
git add tests/AIHelperNET.Application.Tests/Sessions/SaveAnswerFontSizeHandlerTests.cs
git commit -m "feat: add SaveAnswerFontSizeCommand with handler and tests"
```

---

## Task 3: Extend ConversationTurnViewModel

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs`

The ViewModel already has `IMediator mediator` injected and uses `[ObservableProperty]`/`[RelayCommand]` from CommunityToolkit.Mvvm. Add font size state after the existing `[ObservableProperty]` fields.

- [ ] **Step 1: Add the missing using for GetSettingsQuery**

`ConversationTurnViewModel.cs` already has `using AIHelperNET.Application.Sessions.Commands;` (line 3). Add only the Queries namespace at the top alongside it:

```csharp
using AIHelperNET.Application.Sessions.Queries;
```

- [ ] **Step 2: Add the AnswerFontSize property and CanExecute guards**

Inside `ConversationTurnViewModel` (after `[ObservableProperty] private SessionId? _activeSessionId;` around line 90), add:

```csharp
[ObservableProperty] private int _answerFontSize = 12;

private bool CanIncrease() => AnswerFontSize < SaveAnswerFontSizeHandler.Max;
private bool CanDecrease() => AnswerFontSize > SaveAnswerFontSizeHandler.Min;
```

- [ ] **Step 3: Add the two relay commands**

After the `CanIncrease`/`CanDecrease` methods, add:

```csharp
[RelayCommand(CanExecute = nameof(CanIncrease))]
private async Task IncreaseFontSizeAsync()
{
    AnswerFontSize++;
    IncreaseFontSizeCommand.NotifyCanExecuteChanged();
    DecreaseFontSizeCommand.NotifyCanExecuteChanged();
    await mediator.Send(new SaveAnswerFontSizeCommand(AnswerFontSize));
}

[RelayCommand(CanExecute = nameof(CanDecrease))]
private async Task DecreaseFontSizeAsync()
{
    AnswerFontSize--;
    IncreaseFontSizeCommand.NotifyCanExecuteChanged();
    DecreaseFontSizeCommand.NotifyCanExecuteChanged();
    await mediator.Send(new SaveAnswerFontSizeCommand(AnswerFontSize));
}
```

- [ ] **Step 4: Add LoadFontSizeAsync**

After the two commands, add:

```csharp
/// <summary>Restores the persisted answer font size from settings. Call once at startup.</summary>
public async Task LoadFontSizeAsync()
{
    var result = await mediator.Send(new GetSettingsQuery());
    if (result.IsSuccess)
        AnswerFontSize = result.Value.AnswerFontSize;
}
```

- [ ] **Step 5: Build to verify source generation is happy**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
```

Expected: `Build succeeded. 0 Error(s)`

If the source generator reports a name conflict, check that `IncreaseFont SizeCommand` / `DecreaseFontSizeCommand` don't collide with anything in the partial class — they won't, these names are unique.

- [ ] **Step 6: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/ConversationTurnViewModel.cs
git commit -m "feat: add AnswerFontSize property and resize commands to ConversationTurnViewModel"
```

---

## Task 4: Wire XAML — rebind TextBlock and add buttons

**Files:**
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml`

- [ ] **Step 1: Replace the DynamicResource binding on the answer body TextBlock**

Find the TextBlock at line ~258 (the monospace answer body — it has `FontFamily="Cascadia Mono, Consolas"`):

```xml
<TextBlock Text="{Binding LatestVersion.Text}"
           Foreground="{DynamicResource Brush.Foreground.Primary}"
           FontSize="{DynamicResource Font.MD}"
           TextWrapping="Wrap"
           FontFamily="Cascadia Mono, Consolas"
           Margin="0,0,0,4"/>
```

Replace `FontSize="{DynamicResource Font.MD}"` with:

```xml
FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                   RelativeSource={RelativeSource AncestorType=Window}}"
```

So the full element becomes:

```xml
<TextBlock Text="{Binding LatestVersion.Text}"
           Foreground="{DynamicResource Brush.Foreground.Primary}"
           FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                              RelativeSource={RelativeSource AncestorType=Window}}"
           TextWrapping="Wrap"
           FontFamily="Cascadia Mono, Consolas"
           Margin="0,0,0,4"/>
```

Note: This TextBlock is inside an `ItemsControl` `DataTemplate` whose `DataContext` is a `TurnVm`. The `RelativeSource AncestorType=Window` walks up to `MainOverlayWindow`, whose `DataContext` is `MainOverlayWindowContext`. The path `DataContext.ConversationTurn.AnswerFontSize` is the same pattern already used by Copy/Regen/Dismiss commands on lines 266–284.

- [ ] **Step 2: Add the − and + buttons to the action row**

The action `StackPanel` currently ends with a `Resolve` button (line ~280). Add the two new buttons immediately after it, inside the same `StackPanel`:

```xml
<Button Content="Resolve"
        Command="{Binding DataContext.ConversationTurn.ResolveCommand,
            RelativeSource={RelativeSource AncestorType=Window}}"
        CommandParameter="{Binding}"
        Style="{StaticResource ActionBtn}"/>
<Button Content="−"
        Command="{Binding DataContext.ConversationTurn.DecreaseFontSizeCommand,
            RelativeSource={RelativeSource AncestorType=Window}}"
        Style="{StaticResource ActionBtn}"
        ToolTip="Decrease font size"/>
<Button Content="+"
        Command="{Binding DataContext.ConversationTurn.IncreaseFontSizeCommand,
            RelativeSource={RelativeSource AncestorType=Window}}"
        Style="{StaticResource ActionBtn}"
        ToolTip="Increase font size"/>
```

Note: `DecreaseFontSizeCommand` / `IncreaseFontSizeCommand` are the names the CommunityToolkit.Mvvm source generator produces for `[RelayCommand]` methods named `DecreaseFontSizeAsync` / `IncreaseFontSizeAsync` (it strips the `Async` suffix and appends `Command`).

- [ ] **Step 3: Build**

```
dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj
```

Expected: `Build succeeded. 0 Error(s)`

---

## Task 5: Wire startup — call LoadFontSizeAsync before Show()

**Files:**
- Modify: `src/AIHelperNET.App/App.xaml.cs`

- [ ] **Step 1: Add the LoadFontSizeAsync call**

In `App.xaml.cs`, `OnStartup` resolves `turnVm` at line 48:

```csharp
var turnVm = _host.Services.GetRequiredService<ConversationTurnViewModel>();
```

After the three `SetHandler` / `SetHandlers` calls and before `overlay.Show()` (currently line 58), add:

```csharp
await turnVm.LoadFontSizeAsync();
```

So the block looks like:

```csharp
// Wire ConversationTurnSinkAdapter → ConversationTurnViewModel
var turnCreatedSink = _host.Services.GetRequiredService<ConversationTurnSinkAdapter>();
turnCreatedSink.SetHandler((id, question) => turnVm.AddTurn(id, question));

await turnVm.LoadFontSizeAsync();

overlay.Show();
```

- [ ] **Step 2: Build the full solution**

```
dotnet build
```

Expected: `Build succeeded. 0 Error(s)` across all projects.

- [ ] **Step 3: Run all tests**

```
dotnet test
```

Expected: all tests pass, zero failures.

- [ ] **Step 4: Commit everything**

```bash
git add src/AIHelperNET.App/Windows/MainOverlayWindow.xaml
git add src/AIHelperNET.App/App.xaml.cs
git commit -m "feat: wire answer font size buttons and startup restore"
```

---

## Task 6: Manual Verification

Use the `run-aihelper` skill to launch the app, or run:

```
dotnet run --project src/AIHelperNET.App/AIHelperNET.App.csproj
```

- [ ] **Step 1: Verify buttons appear**

Start a session and wait for an answer to arrive. Confirm the answer card action row shows `−` and `+` buttons after the existing Copy / Regen / Dismiss / Resolve buttons.

- [ ] **Step 2: Verify font grows and shrinks**

Click `+` three times. The answer body text (monospace font) should visibly grow with each click. Click `−` three times — it should shrink back.

- [ ] **Step 3: Verify bounds disable the buttons**

Click `+` repeatedly until it greys out (at 20 pt). Then click `−` repeatedly until it greys out (at 9 pt). Both extremes should disable the respective button.

- [ ] **Step 4: Verify persistence**

Set the font to a non-default size (e.g., click `+` twice to reach 14). Close the app. Relaunch it. Start a session and wait for an answer. Confirm the answer text renders at 14 pt, not the default 12 pt.
