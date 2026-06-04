# Adjustable Font Size — Answer Body Panel

**Date:** 2026-06-04  
**Status:** Approved  
**Branch:** feature/adjustable-font-size (to be cut from develop)

---

## Overview

Users need to resize the AI answer body text mid-session without opening Settings. A `−` / `+` button pair is added to each answer card's action row. The chosen size persists across app restarts via `ISettingsStore`.

---

## Scope

- **In scope:** answer body TextBlock only (`LatestVersion.Text`, `FontFamily="Cascadia Mono, Consolas"`).
- **Out of scope:** question header, status label, transcript panel — easy to extend later.

---

## Section 1: Data Model

Add `AnswerFontSize` to `AppSettingsDto` with a default so existing settings files deserialize cleanly:

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

Range: **9–20 pt** (matches existing `Font.XS` floor; 20 is a comfortable ceiling).

---

## Section 2: Application Layer

New command in `Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs`:

```csharp
public sealed record SaveAnswerFontSizeCommand(int FontSize) : IRequest<Result>;

public sealed class SaveAnswerFontSizeHandler(ISettingsStore settingsStore)
    : IRequestHandler<SaveAnswerFontSizeCommand, Result>
{
    internal const int Min = 9;
    internal const int Max = 20;

    public async ValueTask<Result> Handle(SaveAnswerFontSizeCommand cmd, CancellationToken ct)
    {
        var clamped = Math.Clamp(cmd.FontSize, Min, Max);
        var current = await settingsStore.LoadAsync(ct);
        await settingsStore.SaveAsync(current with { AnswerFontSize = clamped }, ct);
        return Result.Ok();
    }
}
```

The handler owns the authoritative clamp. The ViewModel also clamps locally so `CanExecute` guards reflect reality.

---

## Section 3: ViewModel

`ConversationTurnViewModel` gains font size state and two relay commands. No new DI dependencies — `IMediator` is already injected.

```csharp
[ObservableProperty] private int _answerFontSize = 12;

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

private bool CanIncrease() => AnswerFontSize < SaveAnswerFontSizeHandler.Max;
private bool CanDecrease() => AnswerFontSize > SaveAnswerFontSizeHandler.Min;
```

**Startup initialization** — `ConversationTurnViewModel` exposes:

```csharp
public async Task LoadFontSizeAsync()
{
    var result = await mediator.Send(new GetSettingsQuery());
    if (result.IsSuccess)
        AnswerFontSize = result.Value.AnswerFontSize;
}
```

Called once from `App.OnStartup` after DI resolves, before `overlay.Show()`.

---

## Section 4: XAML

**`MainOverlayWindow.xaml` — answer body TextBlock** (currently line 258):

Replace `FontSize="{DynamicResource Font.MD}"` with a direct binding:

```xml
FontSize="{Binding DataContext.ConversationTurn.AnswerFontSize,
                   RelativeSource={RelativeSource AncestorType=Window}}"
```

**Action row** — add after the existing `Dismiss` button:

```xml
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

`CanExecute` guards automatically grey out `−` at 9 pt and `+` at 20 pt.

---

## Section 5: Testing

### `SaveAnswerFontSizeHandlerTests`
- Saves value unchanged when within [9, 20]
- Clamps to `Min` (9) when below range
- Clamps to `Max` (20) when above range
- Preserves all other `AppSettingsDto` fields

### `ConversationTurnViewModelTests`
- `IncreaseFontSizeCommand` increments `AnswerFontSize`
- `DecreaseFontSizeCommand` decrements `AnswerFontSize`
- `CanIncrease` returns false when `AnswerFontSize == Max`
- `CanDecrease` returns false when `AnswerFontSize == Min`
- Both commands dispatch `SaveAnswerFontSizeCommand` via mediator

---

## Files Touched

| File | Change |
|------|--------|
| `Application/Sessions/Dtos/AppSettingsDto.cs` | Add `int AnswerFontSize = 12` parameter |
| `Application/Sessions/Commands/SaveAnswerFontSizeCommand.cs` | New file |
| `App/ViewModels/ConversationTurnViewModel.cs` | Add property, two commands, `LoadFontSizeAsync` |
| `App/Windows/MainOverlayWindow.xaml` | Rebind TextBlock FontSize; add `−`/`+` buttons |
| `App/App.xaml.cs` (OnStartup) | Call `conversationTurn.LoadFontSizeAsync()` |
| `Tests/…/SaveAnswerFontSizeHandlerTests.cs` | New file |
| `Tests/…/ConversationTurnViewModelTests.cs` | New file (or extend existing) |
