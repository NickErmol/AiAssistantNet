# Real-Audio E2E Suite + Answer-Backend Toggle — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Claude/Ollama backend toggle to Settings, and a higher-fidelity "Tier C" E2E suite that drives the real `SessionRunner` audio loop with real Whisper transcription over TTS-generated WAV fixtures (Me=mic, Other=system), asserting per-speaker transcripts and answer-card outcomes, plus one gated Ollama live-answer smoke.

**Architecture:** Two independent workstreams. **Part A** (toggle) is UI/ViewModel only — the `AiBackend` selection is already plumbed through `AppSettingsDto.ActiveBackend` → `AnswerProviderResolver` → the answer commands; only the Settings UI is missing. **Part B/C** (E2E) adds a `WavFileAudioCaptureService` test seam that injects real WAV samples as `AudioFrame`s into the unchanged `SessionRunner`/Whisper/Silero-VAD/pipeline path, a `CapturingTranscriptSink` to surface transcript text, six `RealAudio`-tagged scenario tests, and one `LiveLlm`-tagged Ollama smoke.

**Tech Stack:** .NET 10, xUnit, FluentAssertions, NAudio (WAV read/resample), Whisper.net (Medium model), Silero VAD (ONNX), Windows SAPI (`System.Speech`) for fixture generation, WPF + CommunityToolkit.Mvvm (toggle).

**Reference spec:** `docs/superpowers/specs/2026-06-09-real-audio-e2e-and-backend-toggle-design.md`

---

## Key facts the implementer must know

- **Branch:** work on `feature/real-audio-e2e` (already created, holds the spec + the Photos-close fix). Do **not** push/PR until the user asks — this repo auto-merges PRs on creation.
- **Speaker tagging:** `Speaker.Me` = mic channel, `Speaker.Other` = system/loopback channel (`SessionRunner.RunAsync`, lines 110–113).
- **VAD windowing** (`VadWindowAccumulator`): a speech window is emitted only after **`SilenceFlushCount = 12`** consecutive sub-threshold 512-sample chunks (~375 ms silence) following **≥ `MinChunks = 8`** (~250 ms) of speech. ⇒ **Every fixture WAV must end with ≥ ~700 ms of trailing silence** so its window flushes mid-stream; otherwise it only flushes at end-of-stream and multiple utterances on one channel merge into one window.
- **Whisper filters** (`WhisperTranscriptionService`): drops segments with `< 3` words, `[BLANK_AUDIO]`, known hallucination phrases ("thank you", "please subscribe", …), and near-duplicates (Jaccard ≥ 0.85). ⇒ **Fixture sentences must be ≥ 3 words and not match those phrases.**
- **Merge window:** `SessionRunner`'s consumer merges consecutive **same-speaker** `TranscriptSegment`s whose *arrival* (post-Whisper) is within `segmentMergeWindowMs` into one `TranscriptItem`. With real Whisper this is wall-clock-sensitive; the test harness sets a generous window and uses real `gapMsBefore` delays to separate distinct utterances.
- **Models are already downloaded** (user confirmed) under the data root; the real `WhisperModelProvider`/`SileroModelProvider` resolve them. No download step needed.
- **Test host:** `InterviewHost.CreateAsync()` boots real Application+Infrastructure over in-memory SQLite, overriding AI ports + sinks. Reuse it; the plan extends it (capturing transcript sink + optional real answer provider).

---

## File structure

**Part A — toggle**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs` (add `ActiveBackend` property, load it, save it)
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml` (Backend radios on API Key tab)
- Test: `tests/AIHelperNET.App.Tests/...` if present — **none exists**, so test the VM behaviour via a new `tests/AIHelperNET.Application.Tests` is wrong layer. The VM lives in App (net10.0-windows). Add: `tests/AIHelperNET.UITests/Tests/BackendToggleTests.cs` (FlaUI) for the UI, **plus** a pure VM round-trip via a new test in `tests/AIHelperNET.App.Tests`. **Decision:** there is no App.Tests project; cover the toggle with (1) a FlaUI UI test asserting the radios persist, and (2) keep VM logic trivial. See Task A1 note.

**Part B — harness**
- Create: `tools/generate-audio-fixtures.ps1` (SAPI TTS → 16 kHz mono WAV + trailing silence)
- Create: `tests/AIHelperNET.Integration.Tests/Fixtures/audio/manifest.json` (line text → file name)
- Create: `tests/AIHelperNET.Integration.Tests/Fixtures/audio/*.wav` (generated, committed)
- Create: `tests/AIHelperNET.Integration.Tests/E2E/WavFileAudioCaptureService.cs`
- Create: `tests/AIHelperNET.Integration.Tests/E2E/CapturingTranscriptSink.cs`
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs` (use capturing transcript sink; add `useRealAnswerProvider` flag)
- Create: `tests/AIHelperNET.Integration.Tests/E2E/RealAudioE2ETests.cs` (6 scenarios)
- Create: `tests/AIHelperNET.Integration.Tests/E2E/OllamaLiveAnswerTests.cs` (gated smoke)

**Part D — docs/CI**
- Modify: `.claude/skills/e2e-test/SKILL.md` (or test README) — fixture-gen + `Category!=RealAudio` filter

---

## Part A — Backend toggle

### Task A1: Expose `ActiveBackend` in SettingsViewModel

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`

> Note: there is no unit-test project for the App (WPF) assembly, and the VM change is a trivial property round-trip. It is covered end-to-end by the FlaUI test in Task A3. Keep the VM change minimal and correct.

- [ ] **Step 1: Add the observable property**

In the "API Key tab" region (after `_statusMessage`, around line 25), add:

```csharp
    // ── AI Backend (API Key tab) ──────────────────────────────────
    [ObservableProperty] private AiBackend _activeBackend = AiBackend.Claude;
```

- [ ] **Step 2: Load it in `LoadAsync`**

In `LoadAsync`, after `OverlayOpacity = s.OverlayOpacity;` (line 79), add:

```csharp
        ActiveBackend = s.ActiveBackend;
```

- [ ] **Step 3: Persist it in `SaveSettingsAsync`**

In `SaveSettingsAsync`, replace the first constructor argument (line 132):

```csharp
            current?.ActiveBackend  ?? AiBackend.Claude,
```

with:

```csharp
            ActiveBackend,
```

- [ ] **Step 4: Build the App project**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded, 0 warnings (TreatWarningsAsErrors is on).

- [ ] **Step 5: Commit**

```bash
git add src/AIHelperNET.App/ViewModels/SettingsViewModel.cs
git commit -F - <<'EOF'
feat(settings): expose ActiveBackend in SettingsViewModel

Load and persist the AiBackend choice so the UI toggle can drive the
already-plumbed AnswerProviderResolver. No domain/migration change.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task A2: Add Claude/Ollama radios to the API Key tab

**Files:**
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml`

- [ ] **Step 1: Add the Backend radio group**

In the API Key `TabItem` (the `<StackPanel Margin="8">` ending at line 46), insert before its closing `</StackPanel>` (i.e. after the `StatusMessage` TextBlock, line 45). Uses the globally-registered `EnumToBoolConverter` (App.xaml key `EnumToBoolConverter`), matching the RadioButton pattern in `MainOverlayWindow.xaml:112-150`:

```xml
                    <TextBlock Text="AI Backend" FontWeight="SemiBold"
                               FontSize="{DynamicResource Font.SM}"
                               Foreground="{DynamicResource Brush.Foreground.Primary}"
                               Margin="0,16,0,6"/>
                    <RadioButton Content="Claude" GroupName="Backend"
                                 Foreground="{DynamicResource Brush.Foreground.Primary}"
                                 AutomationProperties.AutomationId="Backend_Claude"
                                 IsChecked="{Binding ActiveBackend,
                                     Converter={StaticResource EnumToBoolConverter},
                                     ConverterParameter=Claude}"/>
                    <RadioButton Content="Ollama (local)" GroupName="Backend"
                                 Margin="0,4,0,0"
                                 Foreground="{DynamicResource Brush.Foreground.Primary}"
                                 AutomationProperties.AutomationId="Backend_Ollama"
                                 IsChecked="{Binding ActiveBackend,
                                     Converter={StaticResource EnumToBoolConverter},
                                     ConverterParameter=Ollama}"/>
```

- [ ] **Step 2: Build the App project**

Run: `dotnet build src/AIHelperNET.App/AIHelperNET.App.csproj`
Expected: Build succeeded (XAML compiles).

- [ ] **Step 3: Commit**

```bash
git add src/AIHelperNET.App/Windows/SettingsWindow.xaml
git commit -F - <<'EOF'
feat(settings): Claude/Ollama backend radios on the API Key tab

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task A3: FlaUI test — backend selection persists

**Files:**
- Create: `tests/AIHelperNET.UITests/Tests/BackendToggleTests.cs`

> Follow existing UITests patterns (`[Collection("UITests")]`, `AppFixture`). Inspect `tests/AIHelperNET.UITests/Tests/SessionLifecycleTests.cs` and the `AppFixture`/page-object helpers for how the Settings window is opened and elements are found by `AutomationId`. If the fixture has no Settings-window helper, open Settings via its launch control and find the radios by the `AutomationId`s added in Task A2 (`Backend_Claude`, `Backend_Ollama`).

- [ ] **Step 1: Write the test**

```csharp
using FluentAssertions;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class BackendToggleTests(AppFixture fixture)
{
    [Fact]
    public void Settings_SelectOllamaBackend_PersistsAcrossReopen()
    {
        // Open Settings → API Key tab, select Ollama, Save, reopen, assert Ollama is checked.
        // Use fixture helpers to open the Settings window; find radios by AutomationId.
        var settings = fixture.OpenSettings();           // adapt to actual fixture API
        settings.SelectTab("API Key");
        var ollama = settings.FindRadio("Backend_Ollama");
        ollama.Click();
        settings.Save();
        settings.Close();

        var reopened = fixture.OpenSettings();
        reopened.SelectTab("API Key");
        reopened.FindRadio("Backend_Ollama").IsChecked.Should().BeTrue();
        reopened.Close();
    }
}
```

- [ ] **Step 2: Adapt to the real fixture API**

Replace the pseudo-helpers (`OpenSettings`, `SelectTab`, `FindRadio`, `Save`, `Close`) with the actual FlaUI calls used elsewhere in the UITests project. Build to verify it compiles:

Run: `dotnet build tests/AIHelperNET.UITests/AIHelperNET.UITests.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Run the test**

Run: `dotnet test tests/AIHelperNET.UITests --filter "FullyQualifiedName~BackendToggle"`
Expected: PASS. (If the Settings window opens on a secondary monitor, see the known `feedback-settings-window` issue — center it.)

- [ ] **Step 4: Commit**

```bash
git add tests/AIHelperNET.UITests/Tests/BackendToggleTests.cs
git commit -F - <<'EOF'
test(ui): backend selection persists across Settings reopen

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## Part B — Real-audio E2E harness

### Task B1: TTS fixture generator + manifest

**Files:**
- Create: `tools/generate-audio-fixtures.ps1`
- Create: `tests/AIHelperNET.Integration.Tests/Fixtures/audio/manifest.json`

- [ ] **Step 1: Write the manifest**

`tests/AIHelperNET.Integration.Tests/Fixtures/audio/manifest.json`:

```json
{
  "trailingSilenceMs": 800,
  "lines": [
    { "file": "other_di.wav",        "text": "What is dependency injection?" },
    { "file": "other_di_part1.wav",  "text": "What is dependency" },
    { "file": "other_di_part2.wav",  "text": "injection in one sentence?" },
    { "file": "me_clarify.wav",      "text": "do you mean constructor injection specifically?" },
    { "file": "other_shorter.wav",   "text": "also keep it short please" },
    { "file": "other_cqrs.wav",      "text": "now explain the C Q R S pattern" },
    { "file": "me_chitchat.wav",     "text": "I am just thinking out loud here" }
  ]
}
```

> All lines are ≥ 3 words and avoid hallucination phrases. "C Q R S" is spelled out so SAPI pronounces the letters.

- [ ] **Step 2: Write the generator script**

`tools/generate-audio-fixtures.ps1`:

```powershell
#Requires -Version 5
# Generates 16 kHz mono WAV fixtures from manifest.json using Windows SAPI,
# appending trailing silence so the Silero VAD flushes each speech window.
[CmdletBinding()]
param(
    [string]$ManifestPath = "$PSScriptRoot/../tests/AIHelperNET.Integration.Tests/Fixtures/audio/manifest.json"
)

Add-Type -AssemblyName System.Speech
$ErrorActionPreference = 'Stop'

$manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$outDir   = Split-Path -Parent $ManifestPath
$silenceMs = [int]$manifest.trailingSilenceMs

# 16 kHz, 16-bit, mono — matches the pipeline's expected input rate (no resample needed).
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, `
       [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, `
       [System.Speech.AudioFormat.AudioChannel]::Mono)

function Append-Silence([string]$wavPath, [int]$ms) {
    # 16000 samples/s * 2 bytes/sample
    $bytes = [int](16000 * 2 * $ms / 1000)
    $silence = New-Object byte[] $bytes
    $fs = [System.IO.File]::Open($wavPath, 'Open', 'ReadWrite')
    try {
        # Patch WAV header sizes (RIFF chunk @4, data chunk @40) then append PCM zeros.
        $fs.Seek(0, 'End') | Out-Null
        $fs.Write($silence, 0, $silence.Length)
        $totalData = [int]$fs.Length - 44
        $fs.Seek(40, 'Begin') | Out-Null
        $w = New-Object System.IO.BinaryWriter($fs)
        $w.Write([int]$totalData)              # data chunk size
        $fs.Seek(4, 'Begin') | Out-Null
        $w.Write([int]($fs.Length - 8))        # RIFF chunk size
        $w.Flush()
    } finally { $fs.Close() }
}

foreach ($line in $manifest.lines) {
    $path = Join-Path $outDir $line.file
    $synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
    try {
        $synth.SetOutputToWaveFile($path, $fmt)
        $synth.Speak($line.text)
    } finally { $synth.Dispose() }
    Append-Silence $path $silenceMs
    Write-Host "Generated $($line.file): '$($line.text)' (+${silenceMs}ms silence)"
}
Write-Host "Done. $($manifest.lines.Count) fixtures written to $outDir"
```

- [ ] **Step 3: Generate the fixtures**

Run: `pwsh -File tools/generate-audio-fixtures.ps1`
Expected: 7 `.wav` files written under `tests/AIHelperNET.Integration.Tests/Fixtures/audio/`. Verify with `Get-ChildItem tests/AIHelperNET.Integration.Tests/Fixtures/audio/*.wav`.

- [ ] **Step 4: Sanity-check one fixture plays as speech**

Run: `Start-Process tests/AIHelperNET.Integration.Tests/Fixtures/audio/other_di.wav` (listen: should say the line, then go silent). Close the player afterward.

- [ ] **Step 5: Mark fixtures to copy to test output**

In `tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`, add (inside an `<ItemGroup>`):

```xml
    <None Include="Fixtures/audio/*" CopyToOutputDirectory="PreserveNewest" />
    <None Include="Fixtures/audio/manifest.json" CopyToOutputDirectory="PreserveNewest" />
```

> Verify the existing csproj globbing doesn't already include them (avoid duplicate-include build errors). If it does, skip this step.

- [ ] **Step 6: Commit**

```bash
git add tools/generate-audio-fixtures.ps1 tests/AIHelperNET.Integration.Tests/Fixtures/audio tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj
git commit -F - <<'EOF'
test(e2e): TTS WAV fixture generator + audio fixtures

Reproducible SAPI generator (tools/generate-audio-fixtures.ps1) with a
text manifest; emits 16 kHz mono WAVs plus trailing silence so the Silero
VAD flushes each speech window. Fixtures committed for offline test runs.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task B2: `WavFileAudioCaptureService`

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/WavFileAudioCaptureService.cs`

- [ ] **Step 1: Write the capture service**

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using NAudio.Wave;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>One scripted audio utterance sourced from a committed WAV fixture.</summary>
/// <param name="Speaker">Channel the utterance is emitted on (Me=mic, Other=loopback).</param>
/// <param name="WavFileName">Fixture file name under Fixtures/audio/.</param>
/// <param name="GapMsBefore">Real wall-clock delay before this utterance's frames, to drive
/// SessionRunner's same-speaker merge window between utterances.</param>
public sealed record WavUtterance(Speaker Speaker, string WavFileName, int GapMsBefore);

/// <summary>
/// Test <see cref="IAudioCaptureService"/> that replays committed WAV fixtures as real 16 kHz
/// mono float <see cref="AudioFrame"/>s, tagged per <see cref="WavUtterance.Speaker"/>, then
/// completes the stream so the SessionRunner loop terminates. Feeds frames in ~32 ms chunks at
/// real time so the Silero VAD and the merge window behave as in production.
/// </summary>
public sealed class WavFileAudioCaptureService(IReadOnlyList<WavUtterance> script) : IAudioCaptureService
{
    private const int ChunkSamples = 512; // 32 ms at 16 kHz — matches VAD chunk size

    /// <summary>Absolute path to the committed fixtures directory in the test output.</summary>
    public static string FixtureDir =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "audio");

    /// <inheritdoc/>
    public async IAsyncEnumerable<AudioFrame> CaptureAsync(
        AudioDeviceSelection selection, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var utt in script)
        {
            if (utt.GapMsBefore > 0)
                await Task.Delay(utt.GapMsBefore, ct);

            var samples = ReadMono16k(Path.Combine(FixtureDir, utt.WavFileName));
            var now = DateTimeOffset.UtcNow;

            for (var offset = 0; offset < samples.Length; offset += ChunkSamples)
            {
                var len = Math.Min(ChunkSamples, samples.Length - offset);
                var chunk = samples[offset..(offset + len)];
                yield return new AudioFrame(chunk, utt.Speaker, now);
                // Pace at ~real time so VAD silence timing and the merge window are realistic.
                await Task.Delay(TimeSpan.FromMilliseconds(32), ct);
            }
        }
    }

    /// <summary>Reads a WAV file as 16 kHz mono float samples (fixtures are already 16 kHz mono).</summary>
    private static float[] ReadMono16k(string path)
    {
        using var reader = new AudioFileReader(path); // exposes float samples via Read(float[])
        var all = new List<float>();
        var buffer = new float[reader.WaveFormat.SampleRate]; // 1 s chunks
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            all.AddRange(buffer[..read]);
        return [.. all];
    }
}
```

> `AudioFileReader` already returns IEEE-float samples; the fixtures are mono 16 kHz so no resample is required. If a fixture is ever non-mono/non-16k, `AudioFileReader` does not resample — keep fixtures 16 kHz mono (the generator enforces this).

- [ ] **Step 2: Build the test project**

Run: `dotnet build tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/WavFileAudioCaptureService.cs
git commit -F - <<'EOF'
test(e2e): WavFileAudioCaptureService injects real WAV audio as frames

Replays committed 16 kHz mono fixtures as Speaker-tagged AudioFrames in
32 ms real-time chunks, driving the real Silero VAD + Whisper path.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task B3: `CapturingTranscriptSink` + host wiring

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/CapturingTranscriptSink.cs`
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs`

- [ ] **Step 1: Write the capturing transcript sink**

```csharp
using System.Collections.Concurrent;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Test <see cref="ITranscriptSink"/> that records every transcript item (speaker + text) so
/// scenarios can assert the per-speaker transcript produced by the real Whisper path.
/// </summary>
public sealed class CapturingTranscriptSink : ITranscriptSink
{
    private readonly ConcurrentQueue<TranscriptItem> _items = new();

    /// <summary>All transcript items pushed, in arrival order.</summary>
    public IReadOnlyList<TranscriptItem> Items => [.. _items];

    /// <summary>Transcript lines for one speaker, in order.</summary>
    public IReadOnlyList<string> TextFor(Speaker speaker) =>
        [.. _items.Where(i => i.Speaker == speaker).Select(i => i.Text)];

    /// <inheritdoc/>
    public void OnTranscriptItem(TranscriptItem item) => _items.Enqueue(item);
}
```

- [ ] **Step 2: Wire it into `InterviewHost`**

In `InterviewHost.cs`: add a property and a `useRealAnswerProvider` parameter.

Replace the field/property block (after `public FakeQuestionBoundaryClassifier Classifier { get; }`):

```csharp
    /// <summary>The capturing transcript sink (singleton) for asserting per-speaker transcripts.</summary>
    public CapturingTranscriptSink Transcripts { get; }
```

Update the constructor signature and body to accept and assign `CapturingTranscriptSink transcripts`:

```csharp
    private InterviewHost(ServiceProvider provider, SqliteConnection keepAlive,
        CapturingAnswerStreamSink sink, FakeQuestionBoundaryClassifier classifier,
        CapturingTranscriptSink transcripts)
    {
        _provider = provider;
        _keepAlive = keepAlive;
        Sink = sink;
        Classifier = classifier;
        Transcripts = transcripts;
    }
```

Change `CreateAsync` to take a flag and use the real provider when asked:

```csharp
    public static async Task<InterviewHost> CreateAsync(bool useRealAnswerProvider = false)
    {
```

Replace the transcript-sink registration line:

```csharp
        services.AddSingleton<ITranscriptSink>(Substitute.For<ITranscriptSink>());
```

with:

```csharp
        var transcripts = new CapturingTranscriptSink();
        services.AddSingleton<ITranscriptSink>(transcripts);
```

Guard the fake answer-provider override so the live test can use the real one:

```csharp
        var fakeProvider = new FakeAnswerProvider();
        if (!useRealAnswerProvider)
            services.AddSingleton<IAnswerProviderResolver>(new FakeAnswerProviderResolver(fakeProvider));
```

Finally, pass `transcripts` into the `return new InterviewHost(...)` call.

- [ ] **Step 3: Build**

Run: `dotnet build tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
Expected: Build succeeded. (Existing tests that used the no-op substitute are unaffected — the capturing sink only records.)

- [ ] **Step 4: Run the existing E2E to confirm no regression**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~E2E"`
Expected: all existing E2E tests still PASS.

- [ ] **Step 5: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/CapturingTranscriptSink.cs tests/AIHelperNET.Integration.Tests/E2E/InterviewHost.cs
git commit -F - <<'EOF'
test(e2e): capturing transcript sink + real-provider host flag

Replaces the no-op ITranscriptSink with a recorder so scenarios can assert
per-speaker transcripts; adds InterviewHost(useRealAnswerProvider) for the
gated live-answer test.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task B4: Real-audio scenario base + Scenario 1 (single Other → one card)

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/RealAudioE2ETests.cs`

This task establishes the harness and validates the real Whisper path end-to-end with the simplest scenario, **calibrating timing** before adding the timing-sensitive scenarios.

- [ ] **Step 1: Write the test class with Scenario 1**

```csharp
using AIHelperNET.App.Services;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Tier C E2E: drives the real SessionRunner + real Whisper transcription over committed WAV
/// fixtures (Me=mic, Other=system), asserting per-speaker transcript and answer-card outcomes.
/// Tagged RealAudio so the default fast run can exclude it (--filter "Category!=RealAudio").
/// Uses the deterministic FakeAnswerProvider; only routing/structure is asserted.
/// </summary>
[Trait("Category", "RealAudio")]
public class RealAudioE2ETests : IAsyncLifetime
{
    private InterviewHost _host = null!;

    public async Task InitializeAsync() => _host = await InterviewHost.CreateAsync();
    public async Task DisposeAsync() => await _host.DisposeAsync();

    // Real Whisper Medium needs a generous merge window (segments arrive after model latency) and
    // long answer/DB timeouts. Tuned empirically in Step 3.
    private const int MergeWindowMs = 1500;
    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromSeconds(60);

    private static readonly AudioDeviceSelection Devices = new("mic", "loopback");
    private const WhisperModelSize Model = WhisperModelSize.Medium;

    private static BoundaryClassificationResult NewQuestion(string text) =>
        new(BoundaryLabel.NewQuestion, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: false, ShouldCreateNewTurn: true,
            NormalizedQuestionText: text, Reason: "scripted");

    private static BoundaryClassificationResult AdditionalRequirement(string text) =>
        new(BoundaryLabel.AdditionalRequirement, 0.95, ShouldGenerateAnswer: true,
            ShouldRefineExistingAnswer: true, ShouldCreateNewTurn: false,
            NormalizedQuestionText: text, Reason: "scripted");

    private async Task<Session> PersistNewSessionAsync()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, DateTimeOffset.UnixEpoch).Value;
        await using var scope = _host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await repo.AddAsync(session, default);
        await uow.SaveChangesAsync(default);
        return session;
    }

    private SessionRunner NewRunner(IReadOnlyList<WavUtterance> script) =>
        new(_host.Services.GetRequiredService<IServiceScopeFactory>(),
            new WavFileAudioCaptureService(script),
            _host.Services.GetRequiredService<ITranscriptionService>(), // REAL Whisper
            _host.Services.GetRequiredService<TranscriptPipelineService>(),
            segmentMergeWindowMs: MergeWindowMs);

    private async Task<Session> ReloadAsync(SessionId id)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await using var scope = _host.Services.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<ISessionRepository>();
                return (await repo.GetAsync(id, default)).Value;
            }
            catch (Microsoft.Data.Sqlite.SqliteException) when (attempt < 5)
            {
                await Task.Delay(50);
            }
        }
    }

    private async Task PollUntilAsync(SessionId id, Func<Session, bool> predicate, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            if (predicate(await ReloadAsync(id))) return;
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Session {id} did not reach the expected DB state in {timeout}.");
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task Scenario1_SingleOtherQuestion_ProducesTranscriptAndOneCard()
    {
        var session = await PersistNewSessionAsync();
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di.wav", GapMsBefore: 0),
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 1, AnswerTimeout);
        await runner.StopAsync();

        // Transcript surfaced for the Other (system) channel.
        _host.Transcripts.TextFor(Speaker.Other).Should()
            .ContainSingle().Which.ToLowerInvariant().Should().Contain("dependency");

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        _host.Sink.Errors.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run Scenario 1**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~Scenario1_SingleOtherQuestion"`
Expected: PASS — a transcript line containing "dependency" and exactly one conversation turn.

- [ ] **Step 3: Calibrate if needed**

If it fails on transcription (empty transcript): listen to `other_di.wav`, confirm it's audible speech + trailing silence, and confirm the Medium model loads (check Serilog output for "SileroVAD: emitting SpeechWindow" and "SessionRunner: segment [Other]"). Adjust `trailingSilenceMs` (Task B1) up to 1000 ms and regenerate if the window never flushes. If it fails on timeout, raise `AnswerTimeout`. Record the working values — later scenarios reuse them.

- [ ] **Step 4: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/RealAudioE2ETests.cs
git commit -F - <<'EOF'
test(e2e): real-audio Tier C harness + single-question scenario

Drives real SessionRunner + Whisper Medium over WAV fixtures; asserts the
Other transcript line and one answer card. RealAudio-tagged.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## Part B (cont.) — Remaining scenarios

> Each scenario is a `[Fact]` added to `RealAudioE2ETests`. Add one, run it, commit. Reuse `me_chitchat.wav` etc. from the manifest.

### Task B5: Scenario 6 (Me chit-chat → no card)

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public async Task Scenario6_MeWithNoPriorQuestion_ProducesNoCard()
    {
        var session = await PersistNewSessionAsync();
        // No classifier result enqueued: a standalone Me utterance must not create a turn.
        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Me, "me_chitchat.wav", GapMsBefore: 0),
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().BeEmpty();
        _host.Sink.Errors.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run** `dotnet test tests/AIHelperNET.Integration.Tests --filter "FullyQualifiedName~Scenario6"` → Expected: PASS.
- [ ] **Step 3: Commit** (`test(e2e): scenario - standalone Me utterance produces no card`).

### Task B6: Scenario 3 (Me clarification refines the card)

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public async Task Scenario3_MeClarification_RefinesSameCard()
    {
        var session = await PersistNewSessionAsync();
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di.wav",     GapMsBefore: 0),
            new WavUtterance(Speaker.Me,    "me_clarify.wav",   GapMsBefore: 3000), // after first answer
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        // One turn, regenerated to >= 2 answer versions once the Me clarification folds in.
        await PollUntilAsync(session.Id,
            s => s.ConversationTurns.Count == 1
                 && s.ConversationTurns[0].AnswerVersions.Count >= 2,
            AnswerTimeout);
        await runner.StopAsync();

        _host.Transcripts.TextFor(Speaker.Me).Should()
            .ContainSingle().Which.ToLowerInvariant().Should().Contain("constructor");

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        reloaded.ConversationTurns[0].AnswerVersions.Count.Should().BeGreaterThanOrEqualTo(2);
        _host.Sink.Errors.Should().BeEmpty();
    }
```

> The `GapMsBefore: 3000` lets the first answer complete before the Me clarification arrives, matching the routing model (Me is a clarifier of a prior Other question). If flaky, increase the gap or poll for the first answer before the Me utterance — but prefer the gap to keep the harness simple.

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Scenario3"` → Expected: PASS.
- [ ] **Step 3: Commit** (`test(e2e): scenario - Me clarification refines the same card`).

### Task B7: Scenario 4 (Other additional requirement → same card refines)

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public async Task Scenario4_OtherAdditionalRequirement_RefinesSameCard()
    {
        var session = await PersistNewSessionAsync();
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));
        _host.Classifier.Enqueue(AdditionalRequirement("also keep it short please"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di.wav",      GapMsBefore: 0),
            new WavUtterance(Speaker.Other, "other_shorter.wav", GapMsBefore: 3000),
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id,
            s => s.ConversationTurns.Count == 1
                 && s.ConversationTurns[0].AnswerVersions.Count >= 2,
            AnswerTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle();
        reloaded.ConversationTurns[0].AnswerVersions.Count.Should().BeGreaterThanOrEqualTo(2);
        _host.Sink.Errors.Should().BeEmpty();
    }
```

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Scenario4"` → Expected: PASS.
- [ ] **Step 3: Commit** (`test(e2e): scenario - Other additional requirement refines card`).

### Task B8: Scenario 5 (two distinct Other questions → two cards)

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public async Task Scenario5_TwoDistinctOtherQuestions_ProduceTwoCards()
    {
        var session = await PersistNewSessionAsync();
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));
        _host.Classifier.Enqueue(NewQuestion("Now explain CQRS?"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di.wav",   GapMsBefore: 0),
            new WavUtterance(Speaker.Other, "other_cqrs.wav", GapMsBefore: 3000), // > merge window
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 2, AnswerTimeout);
        await runner.StopAsync();

        _host.Transcripts.TextFor(Speaker.Other).Should().HaveCountGreaterThanOrEqualTo(2);

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().HaveCount(2);
        _host.Sink.Errors.Should().BeEmpty();
    }
```

> The 3000 ms gap exceeds `MergeWindowMs` (1500), so the two questions' segments arrive far enough apart to stay separate utterances and produce two turns.

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Scenario5"` → Expected: PASS.
- [ ] **Step 3: Commit** (`test(e2e): scenario - two questions produce two cards`).

### Task B9: Scenario 2 (chunked Other question → one card)

- [ ] **Step 1: Add the test**

```csharp
    [Fact]
    public async Task Scenario2_ChunkedOtherQuestion_ProducesOneCard()
    {
        var session = await PersistNewSessionAsync();
        // After the same-speaker merge there is one detection.
        _host.Classifier.Enqueue(NewQuestion("What is dependency injection?"));

        var runner = NewRunner(new[]
        {
            new WavUtterance(Speaker.Other, "other_di_part1.wav", GapMsBefore: 0),
            new WavUtterance(Speaker.Other, "other_di_part2.wav", GapMsBefore: 200), // within merge window
        });
        await runner.StartAsync(session.Id, Devices, Model, "en", AudioSourceMode.Both);

        await runner.WaitForCompletionAsync();
        await PollUntilAsync(session.Id, s => s.ConversationTurns.Count >= 1, AnswerTimeout);
        await runner.StopAsync();

        var reloaded = await ReloadAsync(session.Id);
        reloaded.ConversationTurns.Should().ContainSingle(
            "two same-speaker fragments within the merge window form one transcript item → one card");
        _host.Sink.Errors.Should().BeEmpty();
    }
```

> This is the most timing-sensitive scenario: the two fragments' Whisper segments must arrive within `MergeWindowMs` to merge. They share the single loopback channel and are processed sequentially, so the second segment's arrival lags the first only by its model-processing time. If this proves flaky even after raising `MergeWindowMs`, fall back to a **single** fixture WAV that contains the whole question with a short (<300 ms) internal pause — the VAD then yields one window directly, still validating "a fragmented/disfluent interviewer question yields one card." Document whichever approach is used in a code comment.

- [ ] **Step 2: Run** `--filter "FullyQualifiedName~Scenario2"` → Expected: PASS (apply fallback if flaky).
- [ ] **Step 3: Run all RealAudio scenarios together to check for cross-test interference**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=RealAudio"`
Expected: all six scenarios PASS. (Each test gets a fresh `InterviewHost`, so singleton pipeline state is isolated.)

- [ ] **Step 4: Commit** (`test(e2e): scenario - chunked question produces one card`).

---

## Part C — Gated Ollama live-answer smoke

### Task C1: `OllamaLiveAnswerTests`

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/E2E/OllamaLiveAnswerTests.cs`

- [ ] **Step 1: Write the gated test**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Xunit;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Gated live smoke: runs the REAL Ollama answer provider end to end and asserts a non-empty,
/// well-formed answer comes back. Skips (logs + returns) when Ollama is unreachable, so CI and
/// offline runs stay green without a new test dependency. LiveLlm-tagged → excluded from fast runs.
/// </summary>
[Trait("Category", "LiveLlm")]
public class OllamaLiveAnswerTests
{
    private const string OllamaBaseUrl = "http://localhost:11434";

    private static async Task<bool> OllamaReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync(OllamaBaseUrl);
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    [Fact]
    public async Task RealOllama_AnswersQuestion_WithNonEmptyCard()
    {
        if (!await OllamaReachableAsync())
        {
            Log.Warning("OllamaLiveAnswerTests skipped: {Url} unreachable", OllamaBaseUrl);
            return; // gated skip — no Ollama available
        }

        await using var host = await InterviewHost.CreateAsync(useRealAnswerProvider: true);
        var resolver = host.Services.GetRequiredService<IAnswerProviderResolver>();
        var provider = resolver.Resolve(AiBackend.Ollama);

        var prompt = new AnswerPrompt(
            System: "You are a senior software engineer in a technical interview.",
            User: "In two sentences, what is dependency injection?");

        var sb = new System.Text.StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await foreach (var chunk in provider.StreamAnswerAsync(prompt, cts.Token))
            sb.Append(chunk);

        var answer = sb.ToString().Trim();
        answer.Should().NotBeNullOrWhiteSpace("the real model should return a non-empty answer");
        answer.Length.Should().BeGreaterThan(20);
    }
}
```

> Verify the real `AnswerPrompt` constructor parameter names against `src/AIHelperNET.Application/Answers/AnswerPrompt.cs` and adjust (`System`/`User` shown match `OllamaAnswerProvider`'s usage). The assertion is structure-only (non-empty, length) because LLM output is nondeterministic. The `InterviewHost(useRealAnswerProvider: true)` path (Task B3) leaves the real `AnswerProviderResolver` registered by `AddInfrastructure`.

- [ ] **Step 2: Build**

Run: `dotnet build tests/AIHelperNET.Integration.Tests/AIHelperNET.Integration.Tests.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Run (with Ollama up if available)**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=LiveLlm"`
Expected: PASS if Ollama is running (non-empty answer); otherwise the test returns early (still reported as passed) with a logged skip reason.

- [ ] **Step 4: Commit**

```bash
git add tests/AIHelperNET.Integration.Tests/E2E/OllamaLiveAnswerTests.cs
git commit -F - <<'EOF'
test(e2e): gated Ollama live-answer smoke

Real Ollama provider end to end, structure-only assertions; skips when the
local endpoint is unreachable. LiveLlm-tagged. No new NuGet dependency.

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

---

## Part D — Docs + default-run exclusion

### Task D1: Document fixture generation and the fast-run filter

**Files:**
- Modify: `.claude/skills/e2e-test/SKILL.md` (append a "Real-audio E2E (Tier C)" section)

- [ ] **Step 1: Append documentation**

Add a section covering: (a) regenerate fixtures with `pwsh -File tools/generate-audio-fixtures.ps1` after editing the manifest; (b) run the full deterministic suite fast with `dotnet test --filter "Category!=RealAudio&Category!=LiveLlm"`; (c) run the real-audio suite with `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=RealAudio"` (needs the Whisper Medium model present, ~slow); (d) the Ollama smoke needs a local Ollama at `http://localhost:11434` or it self-skips.

```markdown
## Real-audio E2E (Tier C)

- **Fixtures:** TTS-generated WAVs under `tests/AIHelperNET.Integration.Tests/Fixtures/audio/`.
  Regenerate after editing `manifest.json`: `pwsh -File tools/generate-audio-fixtures.ps1`.
- **Fast run (default, excludes slow real-audio + live LLM):**
  `dotnet test --filter "Category!=RealAudio&Category!=LiveLlm"`
- **Real-audio scenarios (real Whisper Medium, slow):**
  `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=RealAudio"`
- **Ollama live smoke (self-skips if Ollama not running):**
  `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=LiveLlm"`
```

- [ ] **Step 2: Commit**

```bash
git add .claude/skills/e2e-test/SKILL.md
git commit -F - <<'EOF'
docs(e2e): document real-audio fixtures and category filters

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
EOF
```

### Task D2: Final full-suite verification

- [ ] **Step 1: Fast suite is green and excludes the slow tests**

Run: `dotnet test --filter "Category!=RealAudio&Category!=LiveLlm"`
Expected: all previously-green tests pass; the 6 RealAudio + 1 LiveLlm tests are NOT run.

- [ ] **Step 2: Real-audio suite is green**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=RealAudio"`
Expected: 6 scenarios PASS.

- [ ] **Step 3: Confirm no secrets / no raw transcript logging introduced**

Run: `git diff master --stat` and review per the standing security checklist (no API keys; fixtures are synthetic TTS; no transcript logged at Information beyond the existing `SessionRunner`/`SileroVAD` Information lines, which are metadata + already present).

---

## Self-review notes

- **Spec coverage:** Part A toggle (Tasks A1–A3) ✓; capture-seam real-Whisper injection (B2) ✓; TTS WAV fixtures + Medium model (B1, B4) ✓; CapturingTranscriptSink transcript visibility (B3) ✓; all six scenarios (B4–B9) ✓; gated Ollama live answer (C1) ✓; RealAudio trait + fast-run exclusion + docs (B4 trait, D1–D2) ✓; out-of-scope items remain out.
- **Timing risk (called out, not hidden):** Scenarios 2 and 5 depend on real-Whisper segment-arrival timing vs `MergeWindowMs`; B4 Step 3 calibrates, B9 gives a concrete fallback. This is real-world flakiness, mitigated with generous windows, real `gapMsBefore`, polling, and per-test host isolation.
- **Type consistency:** `WavUtterance`, `WavFileAudioCaptureService`, `CapturingTranscriptSink.TextFor`, `InterviewHost.CreateAsync(useRealAnswerProvider)` / `.Transcripts`, and `NewRunner` signatures are used consistently across tasks. `SessionRunner` ctor matches `(IServiceScopeFactory, IAudioCaptureService, ITranscriptionService, TranscriptPipelineService, int)`.
- **Unverified-at-write-time (verify during execution):** `AnswerPrompt` constructor parameter names (C1 Step 1 note); the UITests fixture's Settings-window helper API (A3 Step 2); whether the Integration csproj already globs `Fixtures/**` (B1 Step 5).
```
