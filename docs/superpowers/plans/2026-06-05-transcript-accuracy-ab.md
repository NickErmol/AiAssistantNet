# Transcript Accuracy A+B Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Improve live transcript accuracy by adding rolling Whisper context prompts, tuning VAD window boundaries, upgrading the default model to Medium, and surfacing Large-Turbo / Large-v3 model options in Settings.

**Architecture:** Four independent, sequential changes touching the audio/transcription pipeline and the Settings UI. No new dependencies — all model variants are already in `Whisper.net 1.9.1` (`GgmlType.LargeV3Turbo`, `GgmlType.LargeV3`). Changes are ordered from deepest layer (domain enum) to surface (UI).

**Tech Stack:** .NET 10, Whisper.net 1.9.1, CommunityToolkit.Mvvm, WPF/XAML, xUnit, NSubstitute

---

## File Map

| File | Change |
|------|--------|
| `src/AIHelperNET.Application/Abstractions/AudioFrame.cs` | Add `LargeTurbo`, `Large` to `WhisperModelSize` |
| `src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs` | Two new switch arms in `ModelPath` and `DownloadAsync` |
| `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs` | `MaxWindowFrames` 40→80, `SilenceFramesToFlush` 6→4 |
| `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs` | Move processor build inside window loop; pass `lastEmitted ?? InitialPrompt` |
| `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs` | Default model `Base` → `Medium` |
| `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` | Add `WhisperModel` property; wire `LoadAsync` and `SaveSettingsAsync` |
| `src/AIHelperNET.App/Windows/EnumValues.cs` | Add `WhisperModelSizes` static list |
| `src/AIHelperNET.App/Windows/SettingsWindow.xaml` | Add model `ComboBox` under TRANSCRIPTION section in Audio tab |

---

## Task 1: Expand WhisperModelSize enum and model provider

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/AudioFrame.cs`
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs`

- [ ] **Step 1: Add LargeTurbo and Large to the WhisperModelSize enum**

  Replace the enum in `src/AIHelperNET.Application/Abstractions/AudioFrame.cs` (lines 23-34):

  ```csharp
  /// <summary>Whisper model size to use for transcription.</summary>
  public enum WhisperModelSize
  {
      /// <summary>Tiny model — fastest, lowest accuracy.</summary>
      Tiny,
      /// <summary>Base model.</summary>
      Base,
      /// <summary>Small model.</summary>
      Small,
      /// <summary>Medium model — balanced speed and accuracy.</summary>
      Medium,
      /// <summary>Large-v3 Turbo — distilled Large-v3, ~800 MB, 8× faster than Large.</summary>
      LargeTurbo,
      /// <summary>Large-v3 — maximum accuracy, ~3 GB.</summary>
      Large
  }
  ```

- [ ] **Step 2: Add model paths in WhisperModelProvider**

  In `src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs`, replace the `ModelPath` switch (lines 42-49):

  ```csharp
  private static string ModelPath(WhisperModelSize size)
  {
      var name = size switch
      {
          WhisperModelSize.Tiny       => "ggml-tiny.bin",
          WhisperModelSize.Base       => "ggml-base.bin",
          WhisperModelSize.Small      => "ggml-small.bin",
          WhisperModelSize.Medium     => "ggml-medium.bin",
          WhisperModelSize.LargeTurbo => "ggml-large-v3-turbo.bin",
          WhisperModelSize.Large      => "ggml-large-v3.bin",
          _ => throw new ArgumentOutOfRangeException(nameof(size))
      };
      return Path.Combine(AppPaths.ModelsDir, name);
  }
  ```

- [ ] **Step 3: Add download types in WhisperModelProvider**

  Replace the `DownloadAsync` switch (lines 55-62):

  ```csharp
  var ggmlType = size switch
  {
      WhisperModelSize.Tiny       => GgmlType.Tiny,
      WhisperModelSize.Base       => GgmlType.Base,
      WhisperModelSize.Small      => GgmlType.Small,
      WhisperModelSize.Medium     => GgmlType.Medium,
      WhisperModelSize.LargeTurbo => GgmlType.LargeV3Turbo,
      WhisperModelSize.Large      => GgmlType.LargeV3,
      _ => throw new ArgumentOutOfRangeException(nameof(size))
  };
  ```

- [ ] **Step 4: Build**

  ```
  dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj -c Debug
  ```

  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 5: Run all tests**

  ```
  dotnet test
  ```

  Expected: all existing tests pass, no new failures.

- [ ] **Step 6: Commit**

  ```
  git add src/AIHelperNET.Application/Abstractions/AudioFrame.cs
  git add src/AIHelperNET.Infrastructure/Transcription/WhisperModelProvider.cs
  git commit -m "feat: add LargeTurbo and Large-v3 to WhisperModelSize enum and provider"
  ```

---

## Task 2: Tune VAD window constants

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs`

- [ ] **Step 1: Update window constants**

  Replace lines 9-12 in `VoiceActivityDetector.cs`:

  ```csharp
  private const float EnergyThreshold     = 0.00005f; // tuned for Arctis Nova Pro mic (quiet mic, low gain)
  private const int   SilenceFramesToFlush = 4;       // ~400 ms pause triggers flush (100 ms/frame × 4)
  private const int   MinFramesForSpeech   = 8;       // ~800 ms minimum — blocks noise bursts, allows normal speech
  private const int   MaxWindowFrames      = 80;      // ~8 s max — fits full interview questions without mid-sentence cut
  ```

- [ ] **Step 2: Build**

  ```
  dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj -c Debug
  ```

  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run all tests**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 4: Commit**

  ```
  git add src/AIHelperNET.Infrastructure/Audio/VoiceActivityDetector.cs
  git commit -m "fix: increase VAD max window to 8s and reduce silence flush to 400ms"
  ```

---

## Task 3: Rolling Whisper prompt

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`

The current code builds one `WhisperProcessor` for the entire session with a static prompt. Whisper uses the prompt to carry vocabulary context into the next inference. Moving the build inside the window loop and passing the last emitted segment as the prompt gives each window real conversational context.

- [ ] **Step 1: Replace TranscribeAsync with rolling-prompt version**

  Replace the full `TranscribeAsync` method (lines 21-57) in `WhisperTranscriptionService.cs`:

  ```csharp
  public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
      IAsyncEnumerable<AudioFrame> frames,
      WhisperModelSize model,
      string language,
      [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
  {
      var factory = await models.GetFactoryAsync(model, ct);
      var lang = string.IsNullOrWhiteSpace(language) || language == "auto" ? null : language;

      string? lastEmitted = null;

      await foreach (var window in VoiceActivityDetector.AccumulateSpeechWindows(frames, ct))
      {
          await using var processor = factory.CreateBuilder()
              .WithLanguage(lang ?? "en")
              .WithTemperature(0)
              .WithPrompt(lastEmitted ?? InitialPrompt)
              .WithNoSpeechThreshold(0.6f)
              .WithSingleSegment()
              .Build();

          await foreach (var seg in processor.ProcessAsync(window.Samples, ct))
          {
              if (string.IsNullOrWhiteSpace(seg.Text)) continue;
              if (seg.Text.Contains("[BLANK_AUDIO]", StringComparison.OrdinalIgnoreCase)) continue;
              if (WordCount(seg.Text) < MinWords) continue;
              if (IsKnownHallucination(seg.Text)) continue;
              if (IsNearDuplicate(seg.Text, lastEmitted)) continue;

              lastEmitted = seg.Text;
              yield return new TranscriptSegment(
                  seg.Text.Trim(),
                  window.Speaker,
                  DateTimeOffset.UtcNow,
                  seg.Probability);
          }
      }
  }
  ```

- [ ] **Step 2: Build**

  ```
  dotnet build src/AIHelperNET.Infrastructure/AIHelperNET.Infrastructure.csproj -c Debug
  ```

  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Run all tests**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 4: Commit**

  ```
  git add src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs
  git commit -m "feat: rebuild Whisper processor per window with rolling prompt for vocabulary continuity"
  ```

---

## Task 4: Default model → Medium

**Files:**
- Modify: `src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

- [ ] **Step 1: Change default in JsonSettingsStore**

  In `JsonSettingsStore.cs`, replace line 32:

  ```csharp
  private static AppSettingsDto DefaultSettings() => new(
      AiBackend.Claude,
      WhisperModelSize.Medium,
      Domain.ValueObjects.AnswerSettings.Default,
      Domain.ValueObjects.CodeProfile.Empty,
      null,
      null);
  ```

- [ ] **Step 2: Change fallback in SettingsViewModel**

  In `SettingsViewModel.cs`, replace line 125:

  ```csharp
  current?.WhisperModel   ?? WhisperModelSize.Medium,
  ```

- [ ] **Step 3: Build full solution**

  ```
  dotnet build
  ```

  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Run all tests**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 5: Commit**

  ```
  git add src/AIHelperNET.Infrastructure/Persistence/JsonSettingsStore.cs
  git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
  git commit -m "feat: change default Whisper model from Base to Medium"
  ```

---

## Task 5: Settings UI — WhisperModel picker

**Files:**
- Modify: `src/AIHelperNET.App/Windows/EnumValues.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

- [ ] **Step 1: Add WhisperModelSizes to EnumValues**

  Add to `EnumValues.cs` after the `AnswerStyles` list (after line 38, before the closing `}`):

  ```csharp
  public static IReadOnlyList<EnumOption<WhisperModelSize>> WhisperModelSizes { get; } =
  [
      new("Tiny",        WhisperModelSize.Tiny),
      new("Base",        WhisperModelSize.Base),
      new("Small",       WhisperModelSize.Small),
      new("Medium",      WhisperModelSize.Medium),
      new("Large Turbo", WhisperModelSize.LargeTurbo),
      new("Large",       WhisperModelSize.Large),
  ];
  ```

  Also add the missing using at the top of the file — `WhisperModelSize` lives in `AIHelperNET.Application.Abstractions`:

  ```csharp
  using AIHelperNET.Application.Abstractions;
  using AIHelperNET.Domain.ValueObjects;
  ```

- [ ] **Step 2: Add WhisperModel property to SettingsViewModel**

  In `SettingsViewModel.cs`, add after the `_whisperLanguage` field (line 24):

  ```csharp
  [ObservableProperty] private string _whisperLanguage = "auto";
  [ObservableProperty] private WhisperModelSize _whisperModel = WhisperModelSize.Medium;
  ```

- [ ] **Step 3: Wire WhisperModel in LoadAsync**

  In `SettingsViewModel.cs`, after line 70 (`WhisperLanguage = s.WhisperLanguage;`):

  ```csharp
  WhisperLanguage = s.WhisperLanguage;
  WhisperModel    = s.WhisperModel;
  OverlayOpacity  = s.OverlayOpacity;
  ```

- [ ] **Step 4: Wire WhisperModel in SaveSettingsAsync**

  In `SettingsViewModel.cs`, replace line 125 (the `current?.WhisperModel` line already changed in Task 4):

  ```csharp
  var dto = new AppSettingsDto(
      current?.ActiveBackend ?? AiBackend.Claude,
      WhisperModel,
      new AnswerSettings(AnswerLength, AnswerComplexity, AnswerStyle, AnswerTone, AnswerFormat, OutputLanguage),
      new CodeProfile(NullIfEmpty(ProgrammingLanguage), NullIfEmpty(BackendFramework),
          NullIfEmpty(FrontendFramework), NullIfEmpty(Database), NullIfEmpty(CloudDevOps),
          NullIfEmpty(Messaging), NullIfEmpty(ArchitectureStyle), NullIfEmpty(TestingFramework),
          NullIfEmpty(CustomNotes)),
      NullIfEmpty(SelectedMicDeviceId),
      NullIfEmpty(SelectedLoopbackDeviceId),
      current?.AnswerFontSize ?? 12,
      WhisperLanguage,
      OverlayOpacity)
  {
      Presets = [.. Presets]
  };
  ```

- [ ] **Step 5: Add model ComboBox to SettingsWindow.xaml**

  In `SettingsWindow.xaml`, replace the TRANSCRIPTION section (lines 65-73) with:

  ```xml
  <TextBlock Text="TRANSCRIPTION" Style="{StaticResource SectionLabel}" Margin="0,0,0,6"/>
  <TextBlock Text="Model" Style="{StaticResource FieldLabel}"/>
  <ComboBox ItemsSource="{Binding Source={x:Static local:EnumValues.WhisperModelSizes}}"
            DisplayMemberPath="Display"
            SelectedValuePath="Value"
            SelectedValue="{Binding WhisperModel}"
            Margin="0,0,0,8"/>
  <TextBlock Text="Language" Style="{StaticResource FieldLabel}"/>
  <ComboBox SelectedValue="{Binding WhisperLanguage}"
            SelectedValuePath="Tag">
      <ComboBoxItem Content="Auto-detect" Tag="auto"/>
      <ComboBoxItem Content="English"     Tag="en"/>
      <ComboBoxItem Content="Russian"     Tag="ru"/>
      <ComboBoxItem Content="Polish"      Tag="pl"/>
  </ComboBox>
  ```

- [ ] **Step 6: Build full solution**

  ```
  dotnet build
  ```

  Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 7: Run all tests**

  ```
  dotnet test
  ```

  Expected: all tests pass.

- [ ] **Step 8: Launch app and verify Settings UI**

  ```
  Get-Process -Name "AIHelperNET.App" -ErrorAction SilentlyContinue | Stop-Process -Force
  Start-Process "src\AIHelperNET.App\bin\Debug\net10.0-windows10.0.17763.0\AIHelperNET.App.exe"
  ```

  Open Settings → Audio tab. Confirm:
  - "TRANSCRIPTION" section shows a "Model" ComboBox above the Language picker
  - Dropdown lists: Tiny, Base, Small, Medium, Large Turbo, Large
  - Default selection is Medium (or matches saved settings)
  - Save → reopen Settings → selection persists

- [ ] **Step 9: Commit**

  ```
  git add src/AIHelperNET.App/Windows/EnumValues.cs
  git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
  git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
  git commit -m "feat: add WhisperModel picker to Settings UI with Large Turbo and Large options"
  ```

---

## Post-implementation smoke test

After all tasks are committed:

1. Start a session with Medium model selected
2. Speak a multi-sentence question (5–8 seconds) — confirm it arrives as one segment, not split mid-sentence
3. Speak a second question containing a technical term from the first — confirm Whisper recognises it correctly (rolling prompt effect)
4. Switch to Large Turbo in Settings, save, start a new session — confirm model downloads if absent and session starts
