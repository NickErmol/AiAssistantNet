# Answer-Depth Scaling + Configurable Token Cap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the user set a max-answer-tokens cap in Settings (default 800, range 200–4000) and make the model match answer depth to question difficulty — fixing both over-padded easy answers and mid-word truncation of hard ones.

**Architecture:** App + Application only, no Domain/EF change. The cap is an app-level setting (`AppSettingsDto.MaxAnswerTokens`, settings.json) normalized by a single `AppSettingsDto.Normalized()` helper, read live in `GenerateAnswerCommand` and passed to `PromptBuilderService.Build` (new optional `maxTokens`). Difficulty scaling is one instruction added to the audio `Build` system prompt.

**Tech Stack:** C# record DTO + System.Text.Json (settings.json), Mediator handler, FluentResults, xUnit + FluentAssertions + NSubstitute (existing `AIHelperNET.Application.Tests`), WPF slider (CommunityToolkit.Mvvm `[ObservableProperty]`).

> **Testing note:** the real logic (cap default, clamp/coerce, prompt threading, difficulty-instruction presence/absence) is unit-tested deterministically in `AIHelperNET.Application.Tests` (platform-neutral). The SettingsViewModel property + the XAML slider are trivial mapping/markup — verified by `dotnet build` + manual run, per the App-layer convention. The *behavioral* effect (easy answers actually shorten) is LLM-dependent and verified manually, not in CI.

**Build/test commands:**
- App build: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
- Full build: `dotnet build`
- Tests: `dotnet test tests/AIHelperNET.Application.Tests`
- ⚠️ Stop the running overlay before building the App project — it locks output DLLs (MSB3027): `Get-Process -Name AIHelperNET.App -ErrorAction SilentlyContinue | Stop-Process -Force`

**Namespaces (tests):** `AIHelperNET.Application.Sessions.Dtos` (AppSettingsDto), `AIHelperNET.Application.Sessions.Commands` (SaveSettingsCommand/Handler), `AIHelperNET.Application.Answers` (PromptBuilderService), `AIHelperNET.Application.Abstractions` (AiBackend, WhisperModelSize, ISettingsStore), `AIHelperNET.Domain.ValueObjects` (AnswerSettings, CodeProfile), `AIHelperNET.Domain.Sessions` (ScreenAnalysisMode).

---

## File Structure

- `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs` — **modify**. Add `MaxAnswerTokens`, the
  range/default consts, and the `Normalized()` clamp helper (single source of truth for the cap rules).
- `src/AIHelperNET.Application/Answers/PromptBuilderService.cs` — **modify**. Add `int? maxTokens` to both
  `Build` overloads; add the difficulty instruction to the audio system prompt.
- `src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs` — **modify**. Normalize on save.
- `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs` — **modify**. Pass the cap.
- `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs` — **modify**. Normalize on load.
- `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` — **modify**. `MaxAnswerTokens` property + load + save.
- `src/AIHelperNET.App/Windows/SettingsWindow.xaml` — **modify**. Slider in the ANSWER STYLE section.
- `tests/AIHelperNET.Application.Tests/` — **add**. DTO + prompt-builder + save-handler tests.

---

## Task 1: `AppSettingsDto.MaxAnswerTokens` + `Normalized()` (TDD)

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Create: `tests/AIHelperNET.Application.Tests/AppSettingsDtoTests.cs`

- [ ] **Step 1: Write the failing test.**

`tests/AIHelperNET.Application.Tests/AppSettingsDtoTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class AppSettingsDtoTests
{
    private static AppSettingsDto Base() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public void Default_MaxAnswerTokens_Is800()
        => Base().MaxAnswerTokens.Should().Be(800);

    [Fact]
    public void Normalized_KeepsInRangeValue()
        => (Base() with { MaxAnswerTokens = 1200 }).Normalized().MaxAnswerTokens.Should().Be(1200);

    [Fact]
    public void Normalized_ClampsBelowMin()
        => (Base() with { MaxAnswerTokens = 50 }).Normalized().MaxAnswerTokens.Should().Be(200);

    [Fact]
    public void Normalized_ClampsAboveMax()
        => (Base() with { MaxAnswerTokens = 9000 }).Normalized().MaxAnswerTokens.Should().Be(4000);

    [Fact]
    public void Normalized_CoercesMissingOrZeroToDefault()
        => (Base() with { MaxAnswerTokens = 0 }).Normalized().MaxAnswerTokens.Should().Be(800);
}
```

- [ ] **Step 2: Run — verify it fails to compile (no `MaxAnswerTokens` / `Normalized`).**

Run: `dotnet build tests/AIHelperNET.Application.Tests`
Expected: FAIL — `AppSettingsDto` has no `MaxAnswerTokens` / `Normalized`.

- [ ] **Step 3: Add the field, consts, and helper to `AppSettingsDto`.**

Add `int MaxAnswerTokens = 800` as the **last** positional parameter (after `OverlayOpacity`, so the
existing positional constructions stay valid), and add the consts + `Normalized()` to the body:

```csharp
public sealed record AppSettingsDto(
    AiBackend ActiveBackend,
    WhisperModelSize WhisperModel,
    AnswerSettings AnswerSettings,
    CodeProfile CodeProfile,
    string? MicDeviceId,
    string? LoopbackDeviceId,
    int AnswerFontSize = 12,
    string WhisperLanguage = "auto",
    double OverlayOpacity = 0.75,
    int MaxAnswerTokens = 800)
{
    /// <summary>Default answer-token cap used when unset/legacy.</summary>
    public const int DefaultMaxAnswerTokens = 800;
    /// <summary>Minimum allowed answer-token cap.</summary>
    public const int MinAnswerTokens = 200;
    /// <summary>Maximum allowed answer-token cap.</summary>
    public const int MaxAnswerTokensLimit = 4000;

    /// <summary>Named setting presets for quick profile switching.</summary>
    public IReadOnlyList<ProfilePreset> Presets { get; init; } = [];

    /// <summary>Returns a copy with <see cref="MaxAnswerTokens"/> coerced into the valid range:
    /// missing/non-positive → default 800; otherwise clamped to [200, 4000].</summary>
    public AppSettingsDto Normalized() => this with
    {
        MaxAnswerTokens = MaxAnswerTokens <= 0
            ? DefaultMaxAnswerTokens
            : Math.Clamp(MaxAnswerTokens, MinAnswerTokens, MaxAnswerTokensLimit)
    };
}
```

(The existing `Presets` init property and any XML doc above the record are preserved.)

- [ ] **Step 4: Run the tests — expect GREEN.**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsDtoTests"`
Expected: 5 PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs tests/AIHelperNET.Application.Tests/AppSettingsDtoTests.cs
git commit -m "feat(settings): add MaxAnswerTokens cap (default 800) + Normalized() clamp"
```

---

## Task 2: `PromptBuilderService` — cap param + difficulty instruction (TDD)

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/PromptBuilderService.cs`
- Create: `tests/AIHelperNET.Application.Tests/PromptBuilderDepthTests.cs`

- [ ] **Step 1: Write the failing tests.**

`tests/AIHelperNET.Application.Tests/PromptBuilderDepthTests.cs`:

```csharp
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class PromptBuilderDepthTests
{
    private const string DifficultyMarker = "Match depth to the question";

    [Fact]
    public void Build_UsesExplicitMaxTokens_WhenProvided()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is a primary key?", maxTokens: 1234);
        prompt.MaxTokens.Should().Be(1234);
    }

    [Fact]
    public void Build_FallsBackToLengthMapping_WhenMaxTokensNull()
    {
        // AnswerSettings.Default.Length == ShortLength → MapLengthToTokens == 300
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is a primary key?");
        prompt.MaxTokens.Should().Be(300);
    }

    [Fact]
    public void Build_AudioPrompt_ContainsDifficultyInstruction()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "Explain the CAP theorem");
        prompt.System.Should().Contain(DifficultyMarker);
    }

    [Fact]
    public void ScreenAndFollowUpPrompts_OmitDifficultyInstruction()
    {
        var screen = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default, "some code",
            System.Array.Empty<string>(), ScreenAnalysisMode.General);
        var follow = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, AnswerSettings.Default, "q", "prev answer", "follow up");
        screen.System.Should().NotContain(DifficultyMarker);
        follow.System.Should().NotContain(DifficultyMarker);
    }
}
```

- [ ] **Step 2: Run — verify failure (compile error on `maxTokens:`, plus missing marker).**

Run: `dotnet build tests/AIHelperNET.Application.Tests`
Expected: FAIL — `Build` has no `maxTokens` parameter.

- [ ] **Step 3: Add `maxTokens` to both `Build` overloads and the difficulty instruction.**

In `PromptBuilderService.cs`:

(a) The `DetectedQuestion` overload — add the parameter and forward it:

```csharp
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        DetectedQuestion question,
        string? screenContext = null,
        IReadOnlyList<TranscriptItem>? recentTranscript = null,
        IReadOnlyList<(string Question, string Answer)>? recentQA = null,
        int? maxTokens = null)
        => Build(profile, settings, question.Text, screenContext, recentTranscript, recentQA, maxTokens);
```

(b) The `questionText` overload — add the parameter:

```csharp
    public static AnswerPrompt Build(
        CodeProfile profile,
        AnswerSettings settings,
        string questionText,
        string? screenContext = null,
        IReadOnlyList<TranscriptItem>? recentTranscript = null,
        IReadOnlyList<(string Question, string Answer)>? recentQA = null,
        int? maxTokens = null)
    {
```

(c) Immediately after the existing `AppendStructureGuidance(system, settings.Length);` line, add the
difficulty instruction (reads as a refinement of rule 2):

```csharp
        AppendStructureGuidance(system, settings.Length);
        system.AppendLine("   Match depth to the question's difficulty: for a trivial or factual " +
            "question, answer in 1–2 sentences and skip the bullet scaffold; for a complex design, " +
            "trade-off, or implementation question, use the full structure. Never pad an easy question " +
            "to fill space.");
```

(d) At the end of the `questionText` overload, change the returned cap:

```csharp
        return new AnswerPrompt(
            System: system.ToString(),
            User: user.ToString(),
            OutputLanguage: settings.OutputLanguage,
            MaxTokens: maxTokens ?? MapLengthToTokens(settings.Length));
```

Leave `BuildWithScreenMode`, `BuildScreenFollowUp`, `BuildFollowUp`, and `MapLengthToTokens` unchanged.

- [ ] **Step 4: Run the tests — expect GREEN.**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~PromptBuilderDepthTests"`
Expected: 4 PASS.

- [ ] **Step 5: Commit.**

```bash
git add src/AIHelperNET.Application/Answers/PromptBuilderService.cs tests/AIHelperNET.Application.Tests/PromptBuilderDepthTests.cs
git commit -m "feat(answers): maxTokens cap param + difficulty-scaling instruction (audio Build)"
```

---

## Task 3: Normalize on save (TDD)

**Files:**
- Modify: `src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs`
- Create: `tests/AIHelperNET.Application.Tests/SaveSettingsHandlerTests.cs`

- [ ] **Step 1: Write the failing test.**

`tests/AIHelperNET.Application.Tests/SaveSettingsHandlerTests.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class SaveSettingsHandlerTests
{
    [Fact]
    public async Task Handle_ClampsMaxAnswerTokens_BeforeSaving()
    {
        var store = Substitute.For<ISettingsStore>();
        var handler = new SaveSettingsHandler(store);
        var dto = new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null)
            with { MaxAnswerTokens = 9000 };

        await handler.Handle(new SaveSettingsCommand(dto), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(d => d.MaxAnswerTokens == 4000), Arg.Any<CancellationToken>());
    }
}
```

- [ ] **Step 2: Run — verify it fails (handler saves 9000, not 4000).**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveSettingsHandlerTests"`
Expected: FAIL — the substitute did not receive a dto with `MaxAnswerTokens == 4000`.

- [ ] **Step 3: Normalize in the handler.**

In `SaveSettingsCommand.cs`, change the `Handle` body to normalize before persisting:

```csharp
    public async ValueTask<Result> Handle(SaveSettingsCommand request, CancellationToken cancellationToken)
    {
        await settingsStore.SaveAsync(request.Settings.Normalized(), cancellationToken);
        return Result.Ok();
    }
```

- [ ] **Step 4: Run — expect GREEN.**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~SaveSettingsHandlerTests"`
Expected: PASS.

- [ ] **Step 5: Normalize on load too (so legacy settings.json is fixed when read).**

In `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`, apply `Normalized()` in `LoadAsync`:

```csharp
    public async Task<AppSettingsDto> LoadAsync(CancellationToken ct)
    {
        var path = AppPaths.SettingsFile;
        if (!File.Exists(path)) return DefaultSettings();

        await using var stream = File.OpenRead(path);
        var dto = await JsonSerializer.DeserializeAsync<AppSettingsDto>(stream, Options, ct)
                  ?? DefaultSettings();
        return dto.Normalized();
    }
```

(The coercion logic itself is already covered by `AppSettingsDtoTests`; this is the wiring.)

- [ ] **Step 6: Build to confirm Infrastructure compiles.**

Run: `dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit.**

```bash
git add src/AIHelperNET.Application/Sessions/Commands/SaveSettingsCommand.cs src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs tests/AIHelperNET.Application.Tests/SaveSettingsHandlerTests.cs
git commit -m "feat(settings): normalize MaxAnswerTokens on save and load"
```

---

## Task 4: Thread the cap into answer generation

**Files:**
- Modify: `src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs`

- [ ] **Step 1: Pass `settings.MaxAnswerTokens` to `Build`.**

The handler already loads `var settings = await settingsStore.LoadAsync(cancellationToken);` near the top.
Change the `PromptBuilderService.Build(...)` call (the only caller of the audio overload) to pass the cap:

```csharp
        var prompt = PromptBuilderService.Build(
            session.CodeProfile, session.AnswerSettings, questionText,
            cmd.ScreenContext,
            recentTranscript,
            recentQA,
            maxTokens: settings.MaxAnswerTokens);
```

- [ ] **Step 2: Build the Application project.**

Run: `dotnet build src/AIHelperNET.Application/AIHelperNET.Application.csproj -c Debug`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit.**

```bash
git add src/AIHelperNET.Application/Answers/Commands/GenerateAnswerCommand.cs
git commit -m "feat(answers): use the configured MaxAnswerTokens cap for audio answers"
```

---

## Task 5: SettingsViewModel — `MaxAnswerTokens` property + load + save

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Add the observable property.**

Next to the other answer-style / appearance properties (e.g. right after the `_overlayOpacity` field,
~line 63), add:

```csharp
    [ObservableProperty] private int _maxAnswerTokens = 800;
```

- [ ] **Step 2: Load it in `LoadAsync`.**

After `OverlayOpacity = s.OverlayOpacity;` (~line 82), add:

```csharp
        MaxAnswerTokens = s.MaxAnswerTokens;
```

- [ ] **Step 3: Persist it in `SaveSettingsAsync`.**

In the `new AppSettingsDto(...)` construction, add `MaxAnswerTokens` as the trailing positional argument
(after `OverlayOpacity`), matching the new record parameter order:

```csharp
            current?.AnswerFontSize ?? 12,
            WhisperLanguage,
            OverlayOpacity,
            MaxAnswerTokens)
        {
            Presets = [.. Presets]
        };
```

- [ ] **Step 4: Stop the overlay if running, then build the App project.**

Run (PowerShell): `Get-Process -Name AIHelperNET.App -ErrorAction SilentlyContinue | Stop-Process -Force`
Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s)`.

- [ ] **Step 5: Commit.**

```bash
git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
git commit -m "feat(settings-ui): bind MaxAnswerTokens in SettingsViewModel load/save"
```

---

## Task 6: Settings UI slider

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

- [ ] **Step 1: Add a labelled slider in the ANSWER STYLE section.**

In the "Code Profiles" tab, after the Length `ComboBox` block (the one bound to `AnswerLength`,
~lines 135–140) and before the `Complexity` label, insert (mirrors the existing opacity slider):

```xml
                        <TextBlock Text="Max answer length (tokens)" Style="{StaticResource FieldLabel}"/>
                        <Grid Margin="0,0,0,4">
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width="Auto"/>
                                <ColumnDefinition Width="*"/>
                                <ColumnDefinition Width="Auto"/>
                            </Grid.ColumnDefinitions>
                            <TextBlock Grid.Column="0" Text="200"
                                       Foreground="{DynamicResource Brush.Foreground.Muted}"
                                       FontSize="{DynamicResource Font.XS}" Margin="0,0,6,0"/>
                            <Slider Grid.Column="1"
                                    Minimum="200" Maximum="4000"
                                    SmallChange="50" LargeChange="200"
                                    TickFrequency="50" IsSnapToTickEnabled="True"
                                    AutomationProperties.AutomationId="Settings_MaxAnswerTokens"
                                    Value="{Binding MaxAnswerTokens}"/>
                            <TextBlock Grid.Column="2" Text="4000"
                                       Foreground="{DynamicResource Brush.Foreground.Muted}"
                                       FontSize="{DynamicResource Font.XS}" Margin="6,0,0,0"/>
                        </Grid>
                        <TextBlock Text="{Binding MaxAnswerTokens, StringFormat='{}{0} tokens'}"
                                   HorizontalAlignment="Center"
                                   Foreground="{DynamicResource Brush.Foreground.Secondary}"
                                   FontSize="{DynamicResource Font.XS}" Margin="0,0,0,8"/>
```

Note: `Slider.Value` is a `double` bound to an `int` property — WPF converts automatically, and
`IsSnapToTickEnabled` with `TickFrequency=50` keeps values on clean steps.

- [ ] **Step 2: Build the App project (overlay stopped).**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj -c Debug`
Expected: `Build succeeded. 0 Warning(s)`.

- [ ] **Step 3: Manual check — slider persists.**

Re-run via `run-aihelper`. Open Settings → Code Profiles tab → drag "Max answer length" to e.g. 1500,
Save, reopen Settings. Expected: the slider shows 1500 (persisted to settings.json).

- [ ] **Step 4: Commit.**

```bash
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
git commit -m "feat(settings-ui): Max answer length (tokens) slider"
```

---

## Task 7: Full build + regression sweep

- [ ] **Step 1: Full solution build.**

Run: `dotnet build`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 2: Run the fast test projects.**

Run: `dotnet test tests/AIHelperNET.Application.Tests`
Run: `dotnet test tests/AIHelperNET.Domain.Tests`
Run: `dotnet test tests/AIHelperNET.App.Tests`
Expected: all PASS. Application gains 10 new tests (5 DTO + 4 prompt + 1 save-handler) over the 187
baseline → 197; Domain 104 and App.Tests 10 unchanged.

---

## Final verification & wrap-up

- [ ] **Step 1: Manual end-to-end on the live overlay.**

Re-run via `run-aihelper`. Confirm:
- A **trivial** question (e.g. "What's a primary key?") → a 1–2 sentence answer (difficulty scaling).
- A **hard** question (e.g. "How would you scale a service hitting DB limits?") → full structured answer
  that **completes without truncation** at the 800 default.
- Raising the slider (e.g. to 2000) and asking the hard question again yields an even fuller answer.

- [ ] **Step 2: Use the `finishing-a-development-branch` skill** to PR/merge
  `feature/answer-depth-token-cap` into `develop` (gitflow). Push all commits first (repo auto-merge
  behavior is not guaranteed; merge explicitly when ready).

---

## Self-review (done while writing)

- **Spec coverage:** Cap storage + default 800 → Task 1. Build cap param + fallback → Task 2. Difficulty
  instruction (audio only) → Task 2. Normalize on save → Task 3. Normalize on load (legacy) → Task 3.
  Validation/clamp range → Task 1 (`Normalized`) + Task 3 (applied on save). Handler threading → Task 4.
  Settings VM → Task 5. Settings UI slider → Task 6. Tests → Tasks 1–3. All spec sections mapped.
- **Type consistency:** `MaxAnswerTokens`, `Normalized()`, consts `DefaultMaxAnswerTokens` /
  `MinAnswerTokens` / `MaxAnswerTokensLimit`, the `int? maxTokens` parameter, and the difficulty marker
  string `"Match depth to the question"` are used identically across DTO, prompt builder, handler, VM,
  XAML binding, and tests.
- **No placeholders:** every step shows the actual code/markup/commands.
