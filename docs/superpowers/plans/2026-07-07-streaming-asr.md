# Streaming ASR (Deepgram) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Opt-in Deepgram streaming transcription (`SttProvider` setting) that cuts speech→transcript latency from ~3.5 s to sub-second, with local Whisper as default and automatic per-session fallback.

**Architecture:** Second `ITranscriptionService` implementation (raw `ClientWebSocket` behind a mockable seam) resolved per session via `ISttResolver`, wrapped by `ResilientTranscriptionService` (one reconnect → permanent Whisper fallback, frames buffered through a channel so nothing is dropped). Finals-only: one `TranscriptSegment` per Deepgram `speech_final`.

**Tech Stack:** .NET 10 / WPF, System.Net.WebSockets.ClientWebSocket, System.Text.Json (`JsonDocument`), xUnit + FluentAssertions + NSubstitute, Windows Credential Manager (AdysTech.CredentialManager).

**Spec:** `docs/superpowers/specs/2026-07-07-streaming-asr-design.md`. Branch: `feature/streaming-asr`.

**Repo conventions that apply to every task:** no real API calls in standard suites (live eval is opt-in); local suites are the merge gate (no CI); Conventional Commits; run commands from `D:\work\AIHelperNET`.

**Known pre-existing failures (NOT regressions):** `RealAudioE2ETests.Scenario4` timeout; `ScriptedInterviewE2ETests.Scenario1` + UITests can fail under full-suite parallel load — rerun in isolation before treating as a regression.

---

### Task 1: `TranscriptionOptions` refactor

Fold the Whisper-flavored parameter list into an options record. Pure refactor — compile-driven, no behavior change; existing suites are the test.

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/AudioFrame.cs`
- Modify: `src/AIHelperNET.Application/Abstractions/ITranscriptionService.cs`
- Modify: `src/AIHelperNET.Infrastructure/Transcription/WhisperTranscriptionService.cs`
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs` (~line 96)
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs` (ScriptedTranscriptionService) + any other `ITranscriptionService` implementors/callers found by grep

- [ ] **Step 1: Add the record to `AudioFrame.cs`** (after the `TranscriptSegment` record)

```csharp
/// <summary>Per-session transcription parameters shared by all STT providers.</summary>
/// <param name="Model">Whisper model size (ignored by cloud providers).</param>
/// <param name="Language">BCP-47 language code (e.g. "en") or "auto" for auto-detection.</param>
/// <param name="GlossaryDomains">Enabled glossary domain keys to bias decoding; empty ⇒ no bias.</param>
public sealed record TranscriptionOptions(
    WhisperModelSize Model,
    string Language,
    IReadOnlySet<string> GlossaryDomains);
```

- [ ] **Step 2: Change the interface**

```csharp
/// <summary>Port for transcribing audio frames to text segments.</summary>
public interface ITranscriptionService
{
    /// <summary>Transcribes an audio stream to transcript segments.</summary>
    IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        TranscriptionOptions options,
        CancellationToken ct);
}
```

- [ ] **Step 3: Find every implementor and caller**

Run: `rg -l "TranscribeAsync|ITranscriptionService" src/ tests/`
Expected hits (fix all; grep may surface more): `WhisperTranscriptionService.cs`, `SessionRunner.cs`, `ScriptedAudio.cs`, `SessionControlViewModel.cs`, RealAudio/Scripted E2E hosts that construct `SessionRunner` or call `StartAsync`.

- [ ] **Step 4: Update `WhisperTranscriptionService`**

Signature becomes `TranscribeAsync(IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options, [EnumeratorCancellation] CancellationToken ct)`. In the body replace `model` → `options.Model`, `language` → `options.Language`, `glossaryDomains` → `options.GlossaryDomains` (three body references: `GetFactoryAsync(options.Model, ct)`, the `lang` fallback line, the `BuildPrompt` glossary check + the `WhisperTiming` log's `model` arg).

- [ ] **Step 5: Update `SessionRunner`**

`StartAsync` signature: replace `WhisperModelSize model, string language, ..., IReadOnlySet<string> glossaryDomains` with a single `TranscriptionOptions options` parameter (keep `devices` and `audioSource`):

```csharp
public async Task StartAsync(
    SessionId sessionId,
    AudioDeviceSelection devices,
    TranscriptionOptions options,
    AudioSourceMode audioSource)
```

Thread `options` through `RunAsync` (same replacement) and change both transcription call sites to `.TranscribeAsync(micChannel.Reader.ReadAllAsync(ct), options, ct)` / loopback equivalent.

- [ ] **Step 6: Update `SessionControlViewModel` (~line 96)**

```csharp
await runner.StartAsync(
    result.Value.Id,
    new AudioDeviceSelection(settings?.MicDeviceId, settings?.LoopbackDeviceId),
    new TranscriptionOptions(
        settings?.WhisperModel ?? WhisperModelSize.LargeTurbo,
        settings?.WhisperLanguage ?? "auto",
        glossaryDomains),
    AudioSource);
```

- [ ] **Step 7: Update test fakes** — e.g. `ScriptedTranscriptionService` in `tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs`:

```csharp
public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
    IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options,
    [EnumeratorCancellation] CancellationToken ct)
```

(body unchanged). Fix every other grep hit the same way — callers construct `new TranscriptionOptions(model, language, glossaryDomains)` from the values they previously passed positionally.

- [ ] **Step 8: Build + run the affected suites**

Run: `dotnet build AIHelperNET.sln` → 0 errors. (If the sln filename differs, use the one at repo root.)
Run: `dotnet test tests/AIHelperNET.Application.Tests` → all pass.
Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category!=LiveLlm"` → pass except known pre-existing failures listed above.

- [ ] **Step 9: Commit**

```bash
git add -A && git commit -m "refactor(stt): fold transcription params into TranscriptionOptions record"
```

---

### Task 2: `SttProvider` setting

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/AudioFrame.cs` (enum lives next to `AiBackend`)
- Modify: `src/AIHelperNET.Application/Sessions/Dtos/AppSettingsDto.cs`
- Test: `tests/AIHelperNET.Application.Tests/Sessions/AppSettingsSttProviderTests.cs` (create; if `Sessions/` doesn't exist in that project, put it next to existing AppSettings/DTO tests)

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class AppSettingsSttProviderTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Deserialize_LegacySettingsWithoutSttProvider_DefaultsToWhisper()
    {
        var dto = JsonSerializer.Deserialize<AppSettingsDto>("{}", Web)!;
        dto.SttProvider.Should().Be(SttProvider.Whisper);
    }

    [Fact]
    public void Roundtrip_PreservesSttProvider()
    {
        var original = JsonSerializer.Deserialize<AppSettingsDto>("{}", Web)! with
        {
            SttProvider = SttProvider.Deepgram
        };
        var json = JsonSerializer.Serialize(original, Web);
        JsonSerializer.Deserialize<AppSettingsDto>(json, Web)!
            .SttProvider.Should().Be(SttProvider.Deepgram);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsSttProvider"`
Expected: compile FAIL — `SttProvider` does not exist.

- [ ] **Step 3: Add the enum** (in `AudioFrame.cs`, after the `AiBackend` enum)

```csharp
/// <summary>Selects which speech-to-text provider transcribes session audio.</summary>
public enum SttProvider
{
    /// <summary>Local Whisper — offline, private. The default.</summary>
    Whisper,
    /// <summary>Deepgram cloud streaming — sub-second finals, requires API key.</summary>
    Deepgram
}
```

Add to `AppSettingsDto` as an init-only property (keeps the positional ctor stable and the `with`-expression test working):

```csharp
/// <summary>Speech-to-text provider; Whisper is the offline default.</summary>
public SttProvider SttProvider { get; init; } = SttProvider.Whisper;
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~AppSettingsSttProvider"`
Expected: 2 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): SttProvider setting (Whisper default) on AppSettingsDto"
```

---

### Task 3: Named secrets — `SecretKind` through `ISecretStore`

Every `ISecretStore` method gains a `SecretKind` parameter; existing Anthropic call sites migrate mechanically.

**Files:**
- Modify: `src/AIHelperNET.Application/Abstractions/ISecretStore.cs`
- Modify: `src/AIHelperNET.Infrastructure/Security/WindowsCredentialSecretStore.cs`
- Modify: `src/AIHelperNET.Application/Sessions/Commands/SaveApiKeyCommand.cs`, `DeleteApiKeyCommand.cs`, `src/AIHelperNET.Application/Sessions/Queries/HasApiKeyQuery.cs`
- Modify: every `GetApiKey()`/`HasApiKey()` caller (grep) — Claude providers/classifiers, `SessionReviewLiveTests`, `SettingsViewModel`, etc.
- Test: `tests/AIHelperNET.Application.Tests/Sessions/ApiKeyCommandKindTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class ApiKeyCommandKindTests
{
    [Fact]
    public async Task SaveApiKeyHandler_PassesKindThrough()
    {
        var store = Substitute.For<ISecretStore>();
        store.SaveApiKey(SecretKind.Deepgram, Arg.Any<SecureString>()).Returns(Result.Ok());
        var key = new SecureString(); key.AppendChar('k'); key.MakeReadOnly();

        await new SaveApiKeyHandler(store).Handle(
            new SaveApiKeyCommand(SecretKind.Deepgram, key), CancellationToken.None);

        store.Received(1).SaveApiKey(SecretKind.Deepgram, Arg.Any<SecureString>());
    }

    [Fact]
    public async Task HasApiKeyHandler_PassesKindThrough()
    {
        var store = Substitute.For<ISecretStore>();
        store.HasApiKey(SecretKind.Deepgram).Returns(true);

        var result = await new HasApiKeyHandler(store).Handle(
            new HasApiKeyQuery(SecretKind.Deepgram), CancellationToken.None);

        Assert.True(result.Value);
        store.Received(1).HasApiKey(SecretKind.Deepgram);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Application.Tests --filter "FullyQualifiedName~ApiKeyCommandKind"`
Expected: compile FAIL — `SecretKind` does not exist.

- [ ] **Step 3: Change the interface**

```csharp
namespace AIHelperNET.Application.Abstractions;

/// <summary>Names a stored secret; each kind maps to its own credential-manager entry.</summary>
public enum SecretKind
{
    /// <summary>Anthropic Claude API key.</summary>
    Anthropic,
    /// <summary>Deepgram streaming STT API key.</summary>
    Deepgram
}

/// <summary>Port for storing API keys in the OS secret store.</summary>
public interface ISecretStore
{
    /// <summary>Persists the API key for <paramref name="kind"/>.</summary>
    Result SaveApiKey(SecretKind kind, SecureString key);

    /// <summary>Retrieves the stored API key for <paramref name="kind"/>.</summary>
    Result<SecureString> GetApiKey(SecretKind kind);

    /// <summary>Removes the stored API key for <paramref name="kind"/>.</summary>
    Result DeleteApiKey(SecretKind kind);

    /// <summary>Returns true if an API key is stored for <paramref name="kind"/>.</summary>
    bool HasApiKey(SecretKind kind);
}
```

(`SecretKind` goes in `ISecretStore.cs` — one concept, one file pairing is fine here.)

- [ ] **Step 4: Update the store**

```csharp
public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private static string TargetFor(SecretKind kind) => kind switch
    {
        SecretKind.Anthropic => "AIHelperNET:ClaudeApiKey",   // unchanged — existing stored keys keep working
        SecretKind.Deepgram  => "AIHelperNET:DeepgramApiKey",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    // Each method: replace the `Target` const usage with TargetFor(kind) and add the kind parameter.
}
```

- [ ] **Step 5: Update commands/queries**

```csharp
public sealed record SaveApiKeyCommand(SecretKind Kind, SecureString Key) : IRequest<Result>;
// handler: secretStore.SaveApiKey(command.Kind, command.Key)

public sealed record DeleteApiKeyCommand(SecretKind Kind) : IRequest<Result>;
// handler: secretStore.DeleteApiKey(command.Kind)

public sealed record HasApiKeyQuery(SecretKind Kind) : IRequest<Result<bool>>;
// handler: secretStore.HasApiKey(query.Kind)
```

- [ ] **Step 6: Migrate all call sites mechanically**

Run: `rg -n "GetApiKey\(\)|HasApiKey\(\)|SaveApiKey\(|DeleteApiKey\(|SaveApiKeyCommand\(|DeleteApiKeyCommand\(|HasApiKeyQuery\(" src/ tests/`
Every existing call gets `SecretKind.Anthropic` as first argument (they are all Claude today): providers/classifiers (`ClaudeAnswerProvider`, `HaikuQuestionClassifier`, `SessionReviewAnalyzer`, profile condenser, …), `SettingsViewModel` (`SaveApiKeyCommand(SecretKind.Anthropic, secure)`, `new DeleteApiKeyCommand(SecretKind.Anthropic)`, `new HasApiKeyQuery(SecretKind.Anthropic)`), live tests (`secrets.HasApiKey(SecretKind.Anthropic)`), and NSubstitute setups in existing tests (`GetApiKey(SecretKind.Anthropic).Returns(...)` — or `Arg.Any<SecretKind>()` where the kind is irrelevant).

- [ ] **Step 7: Build + run affected suites**

Run: `dotnet build` then `dotnet test tests/AIHelperNET.Application.Tests` and `dotnet test tests/AIHelperNET.Infrastructure.Tests`
Expected: all pass, including the two new tests.

- [ ] **Step 8: Commit**

```bash
git add -A && git commit -m "refactor(secrets): named secrets via SecretKind; Deepgram credential target"
```

---

### Task 4: Overlay status notifier

Small pub-point for transient header messages ("STT: using local Whisper …"). No existing mechanism — the header today is fixed bindings.

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/IOverlayStatusNotifier.cs`
- Create: `src/AIHelperNET.App/Services/OverlayStatusNotifier.cs`
- Modify: `src/AIHelperNET.App/Windows/MainOverlayWindow.xaml` (~line 39 `Header_StatusText` block) + its DataContext class (`MainOverlayWindowContext` — find via `rg -n "class MainOverlayWindowContext" src/`)
- Modify: `src/AIHelperNET.App/DependencyInjection.cs`
- Test: `tests/AIHelperNET.App.Tests/Services/OverlayStatusNotifierTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.App.Services;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests.Services;

public class OverlayStatusNotifierTests
{
    [Fact]
    public void Notify_SetsMessage_AndRaisesPropertyChanged()
    {
        var sut = new OverlayStatusNotifier();
        var raised = false;
        sut.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(sut.Message);

        sut.Notify("STT: using local Whisper (Deepgram unavailable)");

        sut.Message.Should().Be("STT: using local Whisper (Deepgram unavailable)");
        raised.Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~OverlayStatusNotifier"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`IOverlayStatusNotifier.cs`:

```csharp
namespace AIHelperNET.Application.Abstractions;

/// <summary>Pushes a transient status message to the overlay header (empty string clears it).</summary>
public interface IOverlayStatusNotifier
{
    void Notify(string message);
}
```

`OverlayStatusNotifier.cs`:

```csharp
using AIHelperNET.Application.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AIHelperNET.App.Services;

/// <summary>Holds the overlay header's transient status line; safe to call from any thread.</summary>
public sealed class OverlayStatusNotifier : ObservableObject, IOverlayStatusNotifier
{
    private string _message = string.Empty;

    /// <summary>Current transient message; empty when nothing to show.</summary>
    public string Message { get => _message; private set => SetProperty(ref _message, value); }

    public void Notify(string message)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) Message = message;
        else dispatcher.BeginInvoke(() => Message = message);
    }
}
```

DI (`AddPresentation`):

```csharp
services.AddSingleton<OverlayStatusNotifier>();
services.AddSingleton<IOverlayStatusNotifier>(sp => sp.GetRequiredService<OverlayStatusNotifier>());
```

Expose on the window context: add a ctor-injected property `public OverlayStatusNotifier StatusNotice { get; }` to `MainOverlayWindowContext` (match its existing ctor-property style). In `MainOverlayWindow.xaml`, inside the `Header_StatusText` TextBlock after the last `<Run>`:

```xaml
<Run Text=" "/>
<Run Text="{Binding StatusNotice.Message, Mode=OneWay}"/>
```

- [ ] **Step 4: Run test + build app project**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~OverlayStatusNotifier"` → PASS
Run: `dotnet build src/AIHelperNET.App` → 0 errors (XAML binding compiles).

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(overlay): transient status notifier on header status text"
```

---

### Task 5: Deepgram protocol parsing (pure)

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/DeepgramResultParser.cs`
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/DeepgramUtteranceAssembler.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/DeepgramParsingTests.cs` (create)

Protocol facts (verified against current Deepgram docs): with `interim_results=false` every `Results` message is a finalized chunk; long speech splits into several chunks; `speech_final=true` closes the utterance (endpointing). One `TranscriptSegment` = all chunks since the last `speech_final`, joined.

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class DeepgramParsingTests
{
    private static string ResultsJson(string transcript, float confidence, bool speechFinal, double start) => $$"""
        {"type":"Results","channel_index":[0,1],"duration":1.0,"start":{{start}},
         "is_final":true,"speech_final":{{(speechFinal ? "true" : "false")}},
         "channel":{"alternatives":[{"transcript":"{{transcript}}","confidence":{{confidence}},"words":[]}]}}
        """;

    [Fact]
    public void Parse_ResultsMessage_ExtractsFields()
    {
        var r = DeepgramResultParser.Parse(ResultsJson("hello world", 0.93f, true, 2.5))!;
        r.Transcript.Should().Be("hello world");
        r.Confidence.Should().BeApproximately(0.93f, 0.001f);
        r.SpeechFinal.Should().BeTrue();
        r.StartSeconds.Should().BeApproximately(2.5, 0.001);
    }

    [Theory]
    [InlineData("""{"type":"Metadata","request_id":"x"}""")]
    [InlineData("""{"type":"SpeechStarted"}""")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"Results"}""")]
    public void Parse_NonResultsOrMalformed_ReturnsNull(string json)
        => DeepgramResultParser.Parse(json).Should().BeNull();

    [Fact]
    public void Assembler_JoinsChunksUntilSpeechFinal()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("yeah so my credit card number is two two", 0.9f, false, 0.0))
            .Should().BeNull();
        var utt = sut.Add(new DeepgramResult("two two three three", 0.8f, true, 3.26))!;
        utt.Text.Should().Be("yeah so my credit card number is two two two two three three");
        utt.Confidence.Should().BeApproximately(0.85f, 0.001f);   // average of chunk confidences
        utt.StartSeconds.Should().BeApproximately(0.0, 0.001);    // first chunk's start
    }

    [Fact]
    public void Assembler_EmptySpeechFinal_YieldsNothing_AndResets()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("", 0f, true, 1.0)).Should().BeNull();
        var utt = sut.Add(new DeepgramResult("next utterance starts clean", 0.9f, true, 5.0))!;
        utt.StartSeconds.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public void Assembler_ResetsAfterEmit()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("first", 0.9f, true, 0.0));
        var second = sut.Add(new DeepgramResult("second utterance here", 0.7f, true, 9.9))!;
        second.Text.Should().Be("second utterance here");
        second.Confidence.Should().BeApproximately(0.7f, 0.001f);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~DeepgramParsing"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`DeepgramResultParser.cs`:

```csharp
using System.Text.Json;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>One finalized transcription chunk from a Deepgram Results message.</summary>
public sealed record DeepgramResult(string Transcript, float Confidence, bool SpeechFinal, double StartSeconds);

/// <summary>A complete utterance assembled from one or more chunks (closed by speech_final).</summary>
public sealed record DeepgramUtterance(string Text, float Confidence, double StartSeconds);

/// <summary>Parses Deepgram live-streaming messages; anything but a well-formed Results message is null.</summary>
public static class DeepgramResultParser
{
    public static DeepgramResult? Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("type", out var type) || type.GetString() != "Results") return null;
            if (!root.TryGetProperty("channel", out var channel)) return null;
            if (!channel.TryGetProperty("alternatives", out var alts)
                || alts.ValueKind != JsonValueKind.Array || alts.GetArrayLength() == 0) return null;

            var alt = alts[0];
            var transcript = alt.TryGetProperty("transcript", out var t) ? t.GetString() ?? "" : "";
            var confidence = alt.TryGetProperty("confidence", out var c) ? c.GetSingle() : 0f;
            var speechFinal = root.TryGetProperty("speech_final", out var sf) && sf.GetBoolean();
            var start = root.TryGetProperty("start", out var s) ? s.GetDouble() : 0d;
            return new DeepgramResult(transcript, confidence, speechFinal, start);
        }
        catch (JsonException) { return null; }
    }
}
```

`DeepgramUtteranceAssembler.cs`:

```csharp
namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Accumulates finalized chunks (is_final) into one utterance, emitted when speech_final closes it.
/// Deepgram splits long continuous speech into several finalized chunks before the endpoint fires.
/// </summary>
public sealed class DeepgramUtteranceAssembler
{
    private readonly List<string> _parts = [];
    private readonly List<float> _confidences = [];
    private double _startSeconds = -1;

    /// <summary>Feeds one chunk; returns the completed utterance when speech_final closes it, else null.</summary>
    public DeepgramUtterance? Add(DeepgramResult result)
    {
        if (!string.IsNullOrWhiteSpace(result.Transcript))
        {
            if (_startSeconds < 0) _startSeconds = result.StartSeconds;
            _parts.Add(result.Transcript.Trim());
            _confidences.Add(result.Confidence);
        }

        if (!result.SpeechFinal) return null;
        if (_parts.Count == 0) { Reset(); return null; }

        var utterance = new DeepgramUtterance(
            string.Join(' ', _parts), _confidences.Average(), _startSeconds);
        Reset();
        return utterance;
    }

    private void Reset()
    {
        _parts.Clear();
        _confidences.Clear();
        _startSeconds = -1;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~DeepgramParsing"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): Deepgram Results parser + speech_final utterance assembler"
```

---

### Task 6: PCM conversion + socket seam

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/PcmConverter.cs`
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/IDeepgramSocket.cs`
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/DeepgramClientWebSocket.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/PcmConverterTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class PcmConverterTests
{
    [Fact]
    public void ToLinear16_ConvertsAndClamps()
    {
        var bytes = PcmConverter.ToLinear16([0f, 1f, -1f, 2f, 0.5f]);

        bytes.Should().HaveCount(10);
        BitConverter.ToInt16(bytes, 0).Should().Be(0);
        BitConverter.ToInt16(bytes, 2).Should().Be(short.MaxValue);         // 1.0 → max
        BitConverter.ToInt16(bytes, 4).Should().Be(-short.MaxValue);        // -1.0 → -max (symmetric)
        BitConverter.ToInt16(bytes, 6).Should().Be(short.MaxValue);         // clamped
        BitConverter.ToInt16(bytes, 8).Should().Be((short)Math.Round(0.5f * short.MaxValue));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~PcmConverter"`
Expected: compile FAIL.

- [ ] **Step 3: Implement all three files**

`PcmConverter.cs`:

```csharp
using System.Buffers.Binary;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>Converts [-1,1] float PCM to 16-bit little-endian PCM (linear16), clamping out-of-range samples.</summary>
public static class PcmConverter
{
    public static byte[] ToLinear16(ReadOnlySpan<float> samples)
    {
        var bytes = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var clamped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)MathF.Round(clamped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), value);
        }
        return bytes;
    }
}
```

`IDeepgramSocket.cs`:

```csharp
namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>Thin seam over the Deepgram WebSocket so transcription logic is testable without a network.</summary>
public interface IDeepgramSocket : IAsyncDisposable
{
    Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct);
    Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct);
    Task SendTextAsync(string json, CancellationToken ct);
    /// <summary>Next text message from the server, or null once the socket has closed.</summary>
    Task<string?> ReceiveTextAsync(CancellationToken ct);
}

/// <summary>Creates one socket per transcription stream.</summary>
public interface IDeepgramSocketFactory
{
    IDeepgramSocket Create();
}
```

`DeepgramClientWebSocket.cs`:

```csharp
using System.Net.WebSockets;
using System.Text;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>ClientWebSocket-backed implementation; one instance per transcription stream.</summary>
public sealed class DeepgramClientWebSocket : IDeepgramSocket
{
    private readonly ClientWebSocket _socket = new();

    public Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct)
    {
        _socket.Options.SetRequestHeader("Authorization", $"Token {apiKey}");
        return _socket.ConnectAsync(uri, ct);
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
        => _socket.SendAsync(pcm, WebSocketMessageType.Binary, endOfMessage: true, ct).AsTask();

    public Task SendTextAsync(string json, CancellationToken ct)
        => _socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text,
            endOfMessage: true, ct).AsTask();

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        var builder = new StringBuilder();
        while (true)
        {
            var result = await _socket.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            builder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            if (result.EndOfMessage) return builder.ToString();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", cts.Token);
            }
            catch (Exception) { /* best-effort close; disposal must not throw */ }
        }
        _socket.Dispose();
    }
}

/// <summary>Default factory: a fresh real socket per stream.</summary>
public sealed class DeepgramClientWebSocketFactory : IDeepgramSocketFactory
{
    public IDeepgramSocket Create() => new DeepgramClientWebSocket();
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~PcmConverter"`
Expected: PASS. Also `dotnet build src/AIHelperNET.Infrastructure` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): linear16 PCM converter + Deepgram WebSocket seam"
```

---

### Task 7: `DeepgramTranscriptionService`

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/DeepgramTranscriptionService.cs`
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/DeepgramTranscriptionServiceTests.cs` (create)

Design constraints carried from the codebase: input frames are 16 kHz mono float, single-speaker per call; `MinWords = 3` filter for parity with the Whisper path (short acks are dropped there today); glossary terms via `GlossarySelector.Select` (same assembly), word budget 60 for parity; `CapturedAt` = wall-clock at connect + Deepgram stream-relative `start`.

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading.Channels;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class DeepgramTranscriptionServiceTests
{
    private static string ResultsJson(string transcript, float conf, bool speechFinal, double start) => $$"""
        {"type":"Results","start":{{start}},"is_final":true,"speech_final":{{(speechFinal ? "true" : "false")}},
         "channel":{"alternatives":[{"transcript":"{{transcript}}","confidence":{{conf}}}]}}
        """;

    private static DeepgramTranscriptionService MakeSut(
        FakeDeepgramSocket socket, TimeSpan? keepAlive = null)
    {
        var secrets = Substitute.For<ISecretStore>();
        var key = new SecureString();
        foreach (var c in "dg-fake-key") key.AppendChar(c);
        key.MakeReadOnly();
        secrets.GetApiKey(SecretKind.Deepgram).Returns(Result.Ok(key));

        var glossary = Substitute.For<ITranscriptionGlossaryProvider>();
        glossary.Domains.Returns([]);

        return new DeepgramTranscriptionService(
            new FakeDeepgramSocketFactory(socket), secrets, glossary, keepAlive);
    }

    private static TranscriptionOptions Options(string language = "auto")
        => new(WhisperModelSize.LargeTurbo, language, new HashSet<string>());

    private static async IAsyncEnumerable<AudioFrame> Frames(
        Speaker speaker, int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            yield return new AudioFrame(new float[512], speaker, DateTimeOffset.UtcNow);
            await Task.Yield();
        }
    }

    private static async Task<List<TranscriptSegment>> Collect(
        IAsyncEnumerable<TranscriptSegment> stream)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var s in stream) list.Add(s);
        return list;
    }

    [Fact]
    public async Task EmitsOneSegmentPerSpeechFinal_WithSpeakerAndConfidence()
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("how would you implement dependency injection", 0.97f, true, 1.2));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 3), Options(), CancellationToken.None));

        segments.Should().ContainSingle();
        segments[0].Text.Should().Be("how would you implement dependency injection");
        segments[0].Speaker.Should().Be(Speaker.Other);
        segments[0].Confidence.Should().BeApproximately(0.97f, 0.001f);
    }

    [Fact]
    public async Task JoinsChunksAcrossResultsUntilSpeechFinal()
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("tell me about your experience with", 0.9f, false, 0.5));
        socket.EnqueueMessage(ResultsJson("event driven architecture", 0.8f, true, 3.1));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 2), Options(), CancellationToken.None));

        segments.Should().ContainSingle();
        segments[0].Text.Should().Be("tell me about your experience with event driven architecture");
    }

    [Fact]
    public async Task DropsUtterancesBelowMinWords()  // parity with the Whisper path's MinWords = 3
    {
        var socket = new FakeDeepgramSocket();
        socket.EnqueueMessage(ResultsJson("okay great", 0.99f, true, 0.1));
        var sut = MakeSut(socket);

        var segments = await Collect(sut.TranscribeAsync(
            Frames(Speaker.Other, 1), Options(), CancellationToken.None));

        segments.Should().BeEmpty();
    }

    [Fact]
    public async Task SendsAudioAsLinear16_ThenCloseStreamWhenInputEnds()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 2), Options(), CancellationToken.None));

        socket.SentAudio.Should().HaveCount(2);
        socket.SentAudio[0].Should().HaveCount(1024);           // 512 floats → 1024 bytes
        socket.SentText.Should().Contain(t => t.Contains("CloseStream"));
    }

    [Fact]
    public async Task BuildsUri_WithProtocolParams_AndOmitsLanguageForAuto()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 1), Options("auto"), CancellationToken.None));

        var q = socket.ConnectedUri!.Query;
        q.Should().Contain("model=nova-3").And.Contain("encoding=linear16")
         .And.Contain("sample_rate=16000").And.Contain("channels=1")
         .And.Contain("smart_format=true").And.Contain("interim_results=false")
         .And.Contain("endpointing=300");
        q.Should().NotContain("language=");
        socket.ApiKey.Should().Be("dg-fake-key");
    }

    [Fact]
    public async Task BuildsUri_WithExplicitLanguage()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        await Collect(sut.TranscribeAsync(Frames(Speaker.Me, 1), Options("en"), CancellationToken.None));

        socket.ConnectedUri!.Query.Should().Contain("language=en");
    }

    [Fact]
    public async Task SendsKeepAlive_WhenFramesGoIdle()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket, keepAlive: TimeSpan.FromMilliseconds(30));

        async IAsyncEnumerable<AudioFrame> SlowFrames([EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return new AudioFrame(new float[512], Speaker.Me, DateTimeOffset.UtcNow);
            await Task.Delay(200, ct);
            yield return new AudioFrame(new float[512], Speaker.Me, DateTimeOffset.UtcNow);
        }

        await Collect(sut.TranscribeAsync(SlowFrames(), Options(), CancellationToken.None));

        socket.SentText.Should().Contain(t => t.Contains("KeepAlive"));
    }

    [Fact]
    public async Task EmptyFrameStream_YieldsNothing_WithoutConnecting()
    {
        var socket = new FakeDeepgramSocket();
        var sut = MakeSut(socket);

        async IAsyncEnumerable<AudioFrame> Empty([EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        var segments = await Collect(sut.TranscribeAsync(Empty(), Options(), CancellationToken.None));

        segments.Should().BeEmpty();
        socket.ConnectedUri.Should().BeNull();
    }
}

file sealed class FakeDeepgramSocketFactory(FakeDeepgramSocket socket) : IDeepgramSocketFactory
{
    public IDeepgramSocket Create() => socket;
}

file sealed class FakeDeepgramSocket : IDeepgramSocket
{
    private readonly Channel<string> _incoming = Channel.CreateUnbounded<string>();

    public Uri? ConnectedUri { get; private set; }
    public string? ApiKey { get; private set; }
    public List<byte[]> SentAudio { get; } = [];
    public List<string> SentText { get; } = [];

    public void EnqueueMessage(string json) => _incoming.Writer.TryWrite(json);

    public Task ConnectAsync(Uri uri, string apiKey, CancellationToken ct)
    {
        ConnectedUri = uri;
        ApiKey = apiKey;
        return Task.CompletedTask;
    }

    public Task SendAudioAsync(ReadOnlyMemory<byte> pcm, CancellationToken ct)
    {
        SentAudio.Add(pcm.ToArray());
        return Task.CompletedTask;
    }

    public Task SendTextAsync(string json, CancellationToken ct)
    {
        SentText.Add(json);
        // Server closes after CloseStream once queued results are drained (mirrors real behavior).
        if (json.Contains("CloseStream")) _incoming.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public async Task<string?> ReceiveTextAsync(CancellationToken ct)
    {
        if (!await _incoming.Reader.WaitToReadAsync(ct).ConfigureAwait(false)) return null;
        return _incoming.Reader.TryRead(out var m) ? m : null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

(Namespace for `Speaker` is `AIHelperNET.Domain.Sessions` — adjust the using if the compiler says otherwise; `TranscriptSegment`/`AudioFrame`/`TranscriptionOptions` come from `AIHelperNET.Application.Abstractions`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~DeepgramTranscriptionService"`
Expected: compile FAIL — service does not exist.

- [ ] **Step 3: Implement the service**

```csharp
using System.Runtime.CompilerServices;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Security;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Streams session audio to Deepgram's live WebSocket and yields one segment per endpointed
/// utterance (speech_final). Finals-only: interim results are disabled at the protocol level.
/// Faults (connect failure, socket drop, auth error) propagate to the caller —
/// ResilientTranscriptionService owns retry/fallback policy.
/// </summary>
public sealed class DeepgramTranscriptionService(
    IDeepgramSocketFactory socketFactory,
    ISecretStore secrets,
    ITranscriptionGlossaryProvider glossary,
    TimeSpan? keepAliveInterval = null) : ITranscriptionService
{
    private const int MinWords = 3;              // parity with WhisperTranscriptionService
    private const int KeytermWordBudget = 60;    // parity with the Whisper glossary budget
    private const string KeepAliveJson = """{"type":"KeepAlive"}""";
    private const string CloseStreamJson = """{"type":"CloseStream"}""";
    private readonly TimeSpan _keepAlive = keepAliveInterval ?? TimeSpan.FromSeconds(5);

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // Wait for the first frame before opening the socket: gets the speaker, and an
        // AudioSourceMode-disabled stream never connects (never bills).
        await using var enumerator = frames.GetAsyncEnumerator(ct);
        if (!await enumerator.MoveNextAsync()) yield break;
        var speaker = enumerator.Current.Speaker;

        var keyResult = secrets.GetApiKey(SecretKind.Deepgram);
        if (keyResult.IsFailed)
            throw new InvalidOperationException("No Deepgram API key stored.");

        await using var socket = socketFactory.Create();
        var apiKey = SecureStringHelpers.ConvertToString(keyResult.Value);
        try
        {
            await socket.ConnectAsync(BuildUri(options), apiKey, ct);
        }
        finally
        {
            SecureStringHelpers.ZeroString(apiKey);
        }

        var epoch = DateTimeOffset.UtcNow;   // stream-relative t=0 for CapturedAt mapping
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendTask = SendLoopAsync(socket, enumerator, linked.Token);
        _ = sendTask.ContinueWith(
            _ => linked.Cancel(),   // a dead send loop must not leave the receive loop hanging
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

        var assembler = new DeepgramUtteranceAssembler();
        while (true)
        {
            string? json;
            try
            {
                json = await socket.ReceiveTextAsync(linked.Token);
            }
            catch (OperationCanceledException) when (sendTask.IsFaulted && !ct.IsCancellationRequested)
            {
                break;   // surface the send fault below instead of a bare cancellation
            }
            if (json is null) break;

            var result = DeepgramResultParser.Parse(json);
            if (result is null) continue;
            var utterance = assembler.Add(result);
            if (utterance is null) continue;
            if (utterance.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < MinWords)
                continue;

            Log.Information("DeepgramTiming speaker={Speaker} startSec={Start:F2} conf={Conf:F2}",
                speaker, utterance.StartSeconds, utterance.Confidence);
            yield return new TranscriptSegment(
                utterance.Text, speaker, epoch.AddSeconds(utterance.StartSeconds), utterance.Confidence);
        }

        await sendTask;   // propagate send-side faults as the stream's failure
    }

    private async Task SendLoopAsync(
        IDeepgramSocket socket, IAsyncEnumerator<AudioFrame> frames, CancellationToken ct)
    {
        // Caller has already advanced to the first frame.
        var hasCurrent = true;
        while (hasCurrent)
        {
            await socket.SendAudioAsync(PcmConverter.ToLinear16(frames.Current.Samples), ct);

            // Await the next frame, keeping the socket alive during capture gaps.
            var next = frames.MoveNextAsync().AsTask();
            while (true)
            {
                var completed = await Task.WhenAny(next, Task.Delay(_keepAlive, ct));
                if (completed == next) { hasCurrent = await next; break; }
                await socket.SendTextAsync(KeepAliveJson, ct);
            }
        }
        await socket.SendTextAsync(CloseStreamJson, ct);   // server flushes remaining finals, then closes
    }

    private Uri BuildUri(TranscriptionOptions options)
    {
        // endpointing=300ms: long enough to ride out mid-sentence pauses, far below the
        // ~3.5s batch-Whisper latency this feature exists to beat.
        var sb = new StringBuilder("wss://api.deepgram.com/v1/listen")
            .Append("?model=nova-3&encoding=linear16&sample_rate=16000&channels=1")
            .Append("&smart_format=true&interim_results=false&endpointing=300");

        if (!string.IsNullOrWhiteSpace(options.Language)
            && !options.Language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            sb.Append("&language=").Append(Uri.EscapeDataString(options.Language));

        if (options.GlossaryDomains.Count > 0)
        {
            var terms = GlossarySelector.Select(
                glossary.Domains, options.GlossaryDomains,
                recentContext: string.Empty, KeytermWordBudget);
            foreach (var term in terms)
                sb.Append("&keyterm=").Append(Uri.EscapeDataString(term));
        }

        return new Uri(sb.ToString());
    }
}
```

If `GlossarySelector.Select`'s actual signature differs (check `src/AIHelperNET.Infrastructure/Transcription/GlossarySelector.cs`), match it — it is the same call `JsonTranscriptionGlossaryProvider.BuildPromptSuffix` makes. If `SecureStringHelpers` is `internal` to another namespace path, adjust the using — it lives in `src/AIHelperNET.Infrastructure/Security/SecureStringHelpers.cs`, same assembly.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~DeepgramTranscriptionService"`
Expected: all 8 PASS.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): DeepgramTranscriptionService — streaming finals over WebSocket"
```

---

### Task 8: `ResilientTranscriptionService`

**Files:**
- Create: `src/AIHelperNET.Infrastructure/Transcription/Deepgram/ResilientTranscriptionService.cs`
- Create: `tests/AIHelperNET.Infrastructure.Tests/Transcription/SttTestDoubles.cs` (shared fakes)
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/ResilientTranscriptionServiceTests.cs` (create)

- [ ] **Step 1: Write the shared test doubles** (`SttTestDoubles.cs`)

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

/// <summary>ITranscriptionService driven by a delegate — lets each test script exact stream behavior.</summary>
public sealed class ScriptableStt(
    Func<IAsyncEnumerable<AudioFrame>, CancellationToken, IAsyncEnumerable<TranscriptSegment>> impl)
    : ITranscriptionService
{
    public IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options, CancellationToken ct)
        => impl(frames, ct);
}

/// <summary>Records every frame it consumes, then completes without emitting segments.</summary>
public sealed class FrameRecordingStt : ITranscriptionService
{
    public List<AudioFrame> Consumed { get; } = [];

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames, TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var f in frames.WithCancellation(ct)) Consumed.Add(f);
        yield break;
    }
}
```

- [ ] **Step 2: Write the failing tests**

```csharp
using System.Runtime.CompilerServices;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class ResilientTranscriptionServiceTests
{
    private static TranscriptionOptions Options()
        => new(WhisperModelSize.LargeTurbo, "auto", new HashSet<string>());

    private static async IAsyncEnumerable<AudioFrame> Frames(
        int count, [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var i = 0; i < count; i++)
        {
            // Samples[0] carries the frame index so tests can assert continuity.
            yield return new AudioFrame([i], Speaker.Other, DateTimeOffset.UtcNow);
            await Task.Yield();
        }
    }

    private static TranscriptSegment Seg(string text)
        => new(text, Speaker.Other, DateTimeOffset.UtcNow, 0.9f);

    private static async Task<List<TranscriptSegment>> Collect(IAsyncEnumerable<TranscriptSegment> s)
    {
        var list = new List<TranscriptSegment>();
        await foreach (var x in s) list.Add(x);
        return list;
    }

    [Fact]
    public async Task PrimaryHealthy_PassesSegmentsThrough_NeverTouchesFallback()
    {
        var primary = new ScriptableStt((frames, ct) => Healthy(frames, ct));
        var fallback = new FrameRecordingStt();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier: null);

        var segments = await Collect(sut.TranscribeAsync(Frames(3), Options(), CancellationToken.None));

        segments.Should().ContainSingle(s => s.Text == "all good here friend");
        fallback.Consumed.Should().BeEmpty();

        static async IAsyncEnumerable<TranscriptSegment> Healthy(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await foreach (var _ in frames.WithCancellation(ct)) { }
            yield return new TranscriptSegment("all good here friend", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
        }
    }

    [Fact]
    public async Task PrimaryFaultsTwice_FallsBackToFallback_WithRemainingFrames_AndNotifies()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => FaultAfterConsuming(frames, ct));
        var fallback = new FrameRecordingStt();
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier);

        await Collect(sut.TranscribeAsync(Frames(10), Options(), CancellationToken.None));

        attempts.Should().Be(2);   // initial + one reconnect
        // Attempt 1 consumed frames 0-3, attempt 2 consumed nothing before faulting;
        // fallback gets everything left in the buffer — no frame lost after the fault point.
        fallback.Consumed.Select(f => (int)f.Samples[0]).Should().BeEquivalentTo([4, 5, 6, 7, 8, 9]);
        notifier.Received(1).Notify(Arg.Is<string>(m => m.Contains("Whisper")));

        async IAsyncEnumerable<TranscriptSegment> FaultAfterConsuming(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1)
            {
                var n = 0;
                await foreach (var _ in frames.WithCancellation(ct))
                    if (++n == 4) break;   // consumed 4 frames, then the socket "dies"
            }
            throw new IOException("socket dropped");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    [Fact]
    public async Task PrimaryFaultsOnce_ReconnectSucceeds_NoFallback()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => FlakyThenHealthy(frames, ct));
        var fallback = new FrameRecordingStt();
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier);

        var segments = await Collect(sut.TranscribeAsync(Frames(5), Options(), CancellationToken.None));

        attempts.Should().Be(2);
        segments.Should().ContainSingle(s => s.Text == "recovered on second attempt");
        fallback.Consumed.Should().BeEmpty();
        notifier.DidNotReceiveWithAnyArgs().Notify(default!);

        async IAsyncEnumerable<TranscriptSegment> FlakyThenHealthy(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1) throw new IOException("connect refused");
            await foreach (var _ in frames.WithCancellation(ct)) { }
            yield return new TranscriptSegment("recovered on second attempt", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
        }
    }

    [Fact]
    public async Task SegmentsEmittedBeforeAFault_AreNotLost()
    {
        var attempts = 0;
        var primary = new ScriptableStt((frames, ct) => EmitThenFault(frames, ct));
        var fallback = new FrameRecordingStt();
        var sut = new ResilientTranscriptionService(primary, fallback, notifier: null);

        var segments = await Collect(sut.TranscribeAsync(Frames(4), Options(), CancellationToken.None));

        segments.Should().Contain(s => s.Text == "first utterance made it out");

        async IAsyncEnumerable<TranscriptSegment> EmitThenFault(
            IAsyncEnumerable<AudioFrame> frames, [EnumeratorCancellation] CancellationToken ct = default)
        {
            attempts++;
            if (attempts == 1)
            {
                yield return new TranscriptSegment("first utterance made it out", Speaker.Other, DateTimeOffset.UtcNow, 0.9f);
                throw new IOException("mid-stream drop");
            }
            await foreach (var _ in frames.WithCancellation(ct)) { }
        }
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~ResilientTranscriptionService"`
Expected: compile FAIL.

- [ ] **Step 4: Implement**

```csharp
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using AIHelperNET.Application.Abstractions;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription.Deepgram;

/// <summary>
/// Runs the primary (Deepgram) stream with Whisper as understudy. Frames are pumped through an
/// internal channel; on a primary fault it retries once, then falls back to Whisper for the rest
/// of the session. Unconsumed frames stay queued across the switch, so no audio after the fault
/// point is lost. (The Whisper model is already pre-warmed at app startup regardless of provider.)
/// </summary>
public sealed class ResilientTranscriptionService(
    ITranscriptionService primary,
    ITranscriptionService fallback,
    IOverlayStatusNotifier? notifier) : ITranscriptionService
{
    private const int MaxPrimaryAttempts = 2;   // initial + one reconnect

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        IAsyncEnumerable<AudioFrame> frames,
        TranscriptionOptions options,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var buffer = Channel.CreateUnbounded<AudioFrame>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var frame in frames.WithCancellation(ct))
                    await buffer.Writer.WriteAsync(frame, ct);
            }
            catch (OperationCanceledException) { }
            finally { buffer.Writer.TryComplete(); }
        }, CancellationToken.None);

        for (var attempt = 1; attempt <= MaxPrimaryAttempts; attempt++)
        {
            var faulted = false;
            // Channel readers survive re-enumeration: whatever this attempt doesn't consume
            // stays queued for the next attempt (or the fallback).
            var stream = primary.TranscribeAsync(buffer.Reader.ReadAllAsync(ct), options, ct);
            await using var e = stream.GetAsyncEnumerator(ct);
            while (true)
            {
                TranscriptSegment segment;
                try
                {
                    if (!await e.MoveNextAsync()) break;   // completed normally
                    segment = e.Current;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Primary STT stream faulted (attempt {Attempt}/{Max})",
                        attempt, MaxPrimaryAttempts);
                    faulted = true;
                    break;
                }
                yield return segment;
            }

            if (!faulted)
            {
                await pump;
                yield break;
            }
        }

        Log.Warning("Primary STT unavailable after retry — using local Whisper for the rest of this session");
        notifier?.Notify("STT: using local Whisper (Deepgram unavailable)");

        await foreach (var segment in fallback.TranscribeAsync(buffer.Reader.ReadAllAsync(ct), options, ct))
            yield return segment;
        await pump;
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~ResilientTranscriptionService"`
Expected: 4 PASS.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(stt): resilient wrapper — one reconnect then per-session Whisper fallback"
```

---

### Task 9: `ISttResolver` + DI registration

**Files:**
- Create: `src/AIHelperNET.Application/Abstractions/ISttResolver.cs`
- Create: `src/AIHelperNET.Infrastructure/Transcription/SttResolver.cs`
- Modify: `src/AIHelperNET.Infrastructure/DependencyInjection.cs` (~lines 51–59, transcription block)
- Test: `tests/AIHelperNET.Infrastructure.Tests/Transcription/SttResolverTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Transcription;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class SttResolverTests
{
    private static (SttResolver sut, ITranscriptionService whisper, ISecretStore secrets,
        IOverlayStatusNotifier notifier) Make(bool hasDeepgramKey)
    {
        // The resolver only routes; both services can be stand-ins.
        var whisper = new FrameRecordingStt();
        var deepgram = new FrameRecordingStt();
        var secrets = Substitute.For<ISecretStore>();
        secrets.HasApiKey(SecretKind.Deepgram).Returns(hasDeepgramKey);
        var notifier = Substitute.For<IOverlayStatusNotifier>();
        return (new SttResolver(whisper, deepgram, secrets, notifier), whisper, secrets, notifier);
    }

    [Fact]
    public void Whisper_ReturnsWhisperDirectly_AndClearsNotice()
    {
        var (sut, whisper, _, notifier) = Make(hasDeepgramKey: true);
        sut.Resolve(SttProvider.Whisper).Should().BeSameAs(whisper);
        notifier.Received(1).Notify(string.Empty);
    }

    [Fact]
    public void Deepgram_WithKey_ReturnsResilientWrapper()
    {
        var (sut, _, _, notifier) = Make(hasDeepgramKey: true);
        sut.Resolve(SttProvider.Deepgram).Should().BeOfType<ResilientTranscriptionService>();
        notifier.Received(1).Notify(string.Empty);
    }

    [Fact]
    public void Deepgram_WithoutKey_FallsBackToWhisper_AndNotifies()
    {
        var (sut, whisper, _, notifier) = Make(hasDeepgramKey: false);
        sut.Resolve(SttProvider.Deepgram).Should().BeSameAs(whisper);
        notifier.Received(1).Notify(Arg.Is<string>(m => m.Contains("no Deepgram key")));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SttResolver"`
Expected: compile FAIL.

- [ ] **Step 3: Implement**

`ISttResolver.cs`:

```csharp
namespace AIHelperNET.Application.Abstractions;

/// <summary>Resolves the transcription service for the selected STT provider at session start.</summary>
public interface ISttResolver
{
    ITranscriptionService Resolve(SttProvider provider);
}
```

`SttResolver.cs` — note the ctor takes `ITranscriptionService` twice, so DI must pass the concrete types (see the registration below); making the params interfaces keeps the resolver unit-testable without building the real Whisper stack:

```csharp
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using Serilog;

namespace AIHelperNET.Infrastructure.Transcription;

/// <summary>
/// Whisper resolves directly; Deepgram resolves to the resilient wrapper — or straight to
/// Whisper (with a notice) when no key is stored, so session start never blocks on a missing key.
/// Resolve() also clears any stale fallback notice from a previous session.
/// </summary>
public sealed class SttResolver(
    ITranscriptionService whisper,
    ITranscriptionService deepgram,
    ISecretStore secrets,
    IOverlayStatusNotifier? notifier) : ISttResolver
{
    public ITranscriptionService Resolve(SttProvider provider)
    {
        notifier?.Notify(string.Empty);

        if (provider == SttProvider.Whisper) return whisper;

        if (!secrets.HasApiKey(SecretKind.Deepgram))
        {
            Log.Warning("Deepgram selected but no API key stored — using local Whisper");
            notifier?.Notify("STT: using local Whisper (no Deepgram key)");
            return whisper;
        }

        return new ResilientTranscriptionService(deepgram, whisper, notifier);
    }
}
```

DI — in `src/AIHelperNET.Infrastructure/DependencyInjection.cs`, replace

```csharp
services.AddSingleton<ITranscriptionService, WhisperTranscriptionService>();
```

with

```csharp
services.AddSingleton<WhisperTranscriptionService>();
services.AddSingleton<IDeepgramSocketFactory, DeepgramClientWebSocketFactory>();
services.AddSingleton(sp => new DeepgramTranscriptionService(
    sp.GetRequiredService<IDeepgramSocketFactory>(),
    sp.GetRequiredService<ISecretStore>(),
    sp.GetRequiredService<ITranscriptionGlossaryProvider>()));
services.AddSingleton<ISttResolver>(sp => new SttResolver(
    sp.GetRequiredService<WhisperTranscriptionService>(),
    sp.GetRequiredService<DeepgramTranscriptionService>(),
    sp.GetRequiredService<ISecretStore>(),
    sp.GetService<IOverlayStatusNotifier>()));   // registered by the App layer; null in bare-Infra hosts
```

Then check nothing else resolves the old mapping: `rg -n "ITranscriptionService" src/ --glob "!*Abstractions*"` — if any DI consumer other than `SessionRunner` injects `ITranscriptionService`, route it through the resolver or the concrete Whisper class as appropriate (expected: only `SessionRunner`, handled in Task 10).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/AIHelperNET.Infrastructure.Tests --filter "FullyQualifiedName~SttResolver"`
Expected: 3 PASS. (`FrameRecordingStt` comes from `SttTestDoubles.cs` created in Task 8.)

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): SttResolver + DI wiring for provider selection"
```

---

### Task 10: `SessionRunner` + `SessionControlViewModel` wiring

**Files:**
- Modify: `src/AIHelperNET.App/Services/SessionRunner.cs`
- Modify: `src/AIHelperNET.App/ViewModels/SessionControlViewModel.cs`
- Modify: `tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs` (add `FakeSttResolver`) + every test that constructs `SessionRunner` or calls `StartAsync` (grep)

- [ ] **Step 1: Change `SessionRunner`**

Constructor: replace `ITranscriptionService transcription` with `ISttResolver sttResolver`. `StartAsync` gains the provider:

```csharp
public async Task StartAsync(
    SessionId sessionId,
    AudioDeviceSelection devices,
    TranscriptionOptions options,
    AudioSourceMode audioSource,
    SttProvider sttProvider = SttProvider.Whisper)
```

At the top of `RunAsync` (thread `sttProvider` through as a parameter):

```csharp
var transcription = sttResolver.Resolve(sttProvider);
```

Both `.TranscribeAsync(...)` call sites keep using the local `transcription`.

- [ ] **Step 2: Pass the setting from `SessionControlViewModel`**

In the `StartAsync` call from Task 1 Step 6, add the final argument:

```csharp
    AudioSource,
    settings?.SttProvider ?? SttProvider.Whisper);
```

- [ ] **Step 3: Add `FakeSttResolver` and fix test hosts**

Append to `tests/AIHelperNET.Integration.Tests/E2E/ScriptedAudio.cs`:

```csharp
/// <summary>Test resolver: always returns the supplied service regardless of provider.</summary>
public sealed class FakeSttResolver(ITranscriptionService service) : ISttResolver
{
    public ITranscriptionService Resolve(SttProvider provider) => service;
}
```

Run: `rg -n "new SessionRunner\(" tests/ src/` — wrap each fake/real transcription service argument: `new SessionRunner(scopeFactory, capture, new FakeSttResolver(transcriptionService), pipeline, ...)`. Existing `StartAsync` calls need no edit (the new param defaults to Whisper).

- [ ] **Step 4: Build + run the E2E-adjacent suites**

Run: `dotnet build` → 0 errors.
Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category!=LiveLlm"` and `dotnet test tests/AIHelperNET.App.Tests`
Expected: pass except known pre-existing failures.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat(stt): resolve STT provider per session in SessionRunner"
```

---

### Task 11: Settings UI

**Files:**
- Modify: `src/AIHelperNET.App/ViewModels/SettingsViewModel.cs`
- Modify: `src/AIHelperNET.App/Windows/SettingsWindow.xaml` (Audio tab, lines ~81–129) + `SettingsWindow.xaml.cs`
- Test: `tests/AIHelperNET.App.Tests/ViewModels/SettingsViewModelDeepgramTests.cs` (create; mirror the fixture style of the existing SettingsViewModel tests in that project)

- [ ] **Step 1: Write the failing tests**

```csharp
using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using FluentResults;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests.ViewModels;

public class SettingsViewModelDeepgramTests
{
    // Mirror the constructor-argument style of the existing SettingsViewModel tests in this
    // project (substitute IMediator + the other ctor dependencies the same way they do).
    private static SettingsViewModel MakeSut(IMediator mediator) => new(
        mediator,
        Substitute.For<AIHelperNET.App.Services.IHotkeyApplier>(),
        Substitute.For<ITranscriptionGlossaryProvider>(),
        Substitute.For<IDocumentTextExtractor>(),
        Substitute.For<IProfileCondenser>());

    [Fact]
    public async Task SaveDeepgramKey_SendsCommandWithDeepgramKind_AndClearsInput()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<SaveApiKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        var sut = MakeSut(mediator);
        sut.DeepgramKeyInput = "dg-secret";

        await sut.SaveDeepgramKeyCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<SaveApiKeyCommand>(c => c.Kind == SecretKind.Deepgram), Arg.Any<CancellationToken>());
        Assert.Equal(string.Empty, sut.DeepgramKeyInput);
    }

    [Fact]
    public async Task DeleteDeepgramKey_SendsCommandWithDeepgramKind()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DeleteApiKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        var sut = MakeSut(mediator);

        await sut.DeleteDeepgramKeyCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<DeleteApiKeyCommand>(c => c.Kind == SecretKind.Deepgram), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SelectingDeepgram_TogglesKeyPanelVisibility()
    {
        var sut = MakeSut(Substitute.For<IMediator>());
        Assert.False(sut.IsDeepgramSelected);
        sut.SttProvider = SttProvider.Deepgram;
        Assert.True(sut.IsDeepgramSelected);
    }
}
```

(If `MakeSut`'s dependency list drifts from the real ctor, copy the arrangement from the existing `SettingsViewModel` tests — the assertions are what matter.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~SettingsViewModelDeepgram"`
Expected: compile FAIL.

- [ ] **Step 3: Add ViewModel members**

```csharp
// ── Transcription provider (Audio tab) ────────────────────────
[ObservableProperty] private SttProvider _sttProvider = SttProvider.Whisper;
[ObservableProperty] private bool _isDeepgramSelected;
[ObservableProperty] private string _deepgramKeyInput = string.Empty;
[ObservableProperty] private string _deepgramStatusMessage = string.Empty;

partial void OnSttProviderChanged(SttProvider value)
    => IsDeepgramSelected = value == SttProvider.Deepgram;

[RelayCommand]
private async Task SaveDeepgramKeyAsync()
{
    if (string.IsNullOrWhiteSpace(DeepgramKeyInput)) return;
    using var secure = new System.Security.SecureString();
    foreach (var c in DeepgramKeyInput) secure.AppendChar(c);
    secure.MakeReadOnly();
    var result = await mediator.Send(new SaveApiKeyCommand(SecretKind.Deepgram, secure));
    DeepgramStatusMessage = result.IsSuccess
        ? "Deepgram key saved ✓" : $"Error: {string.Join(", ", result.Errors)}";
    DeepgramKeyInput = string.Empty;
}

[RelayCommand]
private async Task DeleteDeepgramKeyAsync()
{
    var result = await mediator.Send(new DeleteApiKeyCommand(SecretKind.Deepgram));
    DeepgramStatusMessage = result.IsSuccess
        ? "Deepgram key deleted." : $"Error: {string.Join(", ", result.Errors)}";
}
```

In `LoadAsync` add `SttProvider = s.SttProvider;` and (after the API-key status refresh) `DeepgramStatusMessage = (await mediator.Send(new HasApiKeyQuery(SecretKind.Deepgram))).ValueOrDefault ? "Deepgram key is stored ✓" : "No Deepgram key stored.";`. In `SaveSettingsAsync` include the property on the DTO: `SttProvider = SttProvider` in the init block.

- [ ] **Step 4: Add XAML (Audio tab) + code-behind**

At the end of the Audio tab's StackPanel:

```xaml
<TextBlock Text="Transcription provider" FontWeight="SemiBold" Margin="0,16,0,6"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="Local Whisper (offline, private)" GroupName="Stt"
             AutomationProperties.AutomationId="Stt_Whisper"
             IsChecked="{Binding SttProvider, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Whisper}"/>
<RadioButton Style="{StaticResource RadioBtn}" Content="Deepgram streaming (cloud, sub-second)" GroupName="Stt"
             AutomationProperties.AutomationId="Stt_Deepgram"
             IsChecked="{Binding SttProvider, Converter={StaticResource EnumToBoolConverter}, ConverterParameter=Deepgram}"/>

<StackPanel Visibility="{Binding IsDeepgramSelected, Converter={StaticResource BoolToVis}}">
    <TextBlock Text="Deepgram API Key" FontWeight="SemiBold" Margin="0,12,0,6"/>
    <DockPanel>
        <Button Content="Delete" DockPanel.Dock="Right" Width="60"
                Command="{Binding DeleteDeepgramKeyCommand}"/>
        <Button Content="Save" DockPanel.Dock="Right" Width="60" Margin="0,0,6,0"
                Command="{Binding SaveDeepgramKeyCommand}"/>
        <Border BorderThickness="1" CornerRadius="3" Padding="1">
            <PasswordBox x:Name="DeepgramKeyBox"
                         AutomationProperties.AutomationId="DeepgramKeyBox"
                         PasswordChanged="DeepgramKeyBox_PasswordChanged"/>
        </Border>
    </DockPanel>
    <TextBlock Text="{Binding DeepgramStatusMessage}" Margin="0,8,0,0"
               Foreground="{DynamicResource Brush.Semantic.Active}"/>
    <TextBlock TextWrapping="Wrap" Margin="0,8,0,0"
               Foreground="{DynamicResource Brush.Foreground.Secondary}"
               Text="Cloud transcription streams both microphone and interviewer audio to Deepgram for the whole session (≈$0.90/hour across both channels). If the connection drops, the session falls back to local Whisper automatically. Whisper runs fully offline."/>
</StackPanel>
```

If `BoolToVis` isn't already in the window resources, add `<BooleanToVisibilityConverter x:Key="BoolToVis"/>` there. Code-behind (`SettingsWindow.xaml.cs`), mirroring `ApiKeyBox_PasswordChanged`:

```csharp
private void DeepgramKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
{
    if (DataContext is SettingsViewModel vm)
        vm.DeepgramKeyInput = DeepgramKeyBox.Password;
}
```

- [ ] **Step 5: Run tests + build**

Run: `dotnet test tests/AIHelperNET.App.Tests --filter "FullyQualifiedName~SettingsViewModelDeepgram"` → 3 PASS.
Run: `dotnet build src/AIHelperNET.App` → 0 errors.

- [ ] **Step 6: Commit**

```bash
git add -A && git commit -m "feat(settings): STT provider selection + Deepgram key entry with privacy note"
```

---

### Task 12: Opt-in live eval

**Files:**
- Create: `tests/AIHelperNET.Integration.Tests/Eval/DeepgramLiveTests.cs`

Mirrors `SessionReviewLiveTests`: trait-gated, skips (early return) without a stored key, hits the real API when `AIHelperNET:DeepgramApiKey` exists in Windows Credential Manager.

- [ ] **Step 1: Check what `other_di.wav` asserts** — open `RealAudioE2ETests` and copy the phrase it expects from `other_di.wav` (e.g. the dependency-injection question text). Use that phrase below.

- [ ] **Step 2: Write the test**

```csharp
using System.Diagnostics;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.Security;
using AIHelperNET.Infrastructure.Transcription;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

[Trait("Category", "LiveStt")]
public class DeepgramLiveTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Deepgram_StreamsFixtureWav_ProducesExpectedTranscript_Fast()
    {
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey(SecretKind.Deepgram))
        {
            output.WriteLine("Skipped: no Deepgram API key in Windows Credential Manager " +
                "(target 'AIHelperNET:DeepgramApiKey').");
            return;
        }

        var capture = new E2E.WavFileAudioCaptureService(
            [new E2E.WavUtterance(Speaker.Other, "other_di.wav", GapMsBefore: 0)]);
        var sut = new DeepgramTranscriptionService(
            new DeepgramClientWebSocketFactory(), secrets, new JsonTranscriptionGlossaryProvider());

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var sw = Stopwatch.StartNew();
        var segments = new List<TranscriptSegment>();
        await foreach (var seg in sut.TranscribeAsync(
            capture.CaptureAsync(new AudioDeviceSelection(null, null), cts.Token),
            new TranscriptionOptions(WhisperModelSize.LargeTurbo, "en", new HashSet<string>()),
            cts.Token))
        {
            output.WriteLine($"[{sw.ElapsedMilliseconds} ms] conf={seg.Confidence:F2} {seg.Text}");
            segments.Add(seg);
        }

        segments.Should().NotBeEmpty("Deepgram should produce at least one endpointed utterance");
        var transcript = string.Join(" ", segments.Select(s => s.Text)).ToLowerInvariant();
        transcript.Should().Contain("dependency injection");   // ← align with RealAudioE2ETests' phrase for this fixture
        segments.All(s => s.Confidence is > 0f and <= 1f).Should().BeTrue();
    }
}
```

(Adjust namespaces for `WavFileAudioCaptureService`/`WavUtterance` to their actual location in `E2E/`.)

- [ ] **Step 3: Verify it skips cleanly without a key**

Run: `dotnet test tests/AIHelperNET.Integration.Tests --filter "Category=LiveStt"`
Expected: 1 passed (skip-by-early-return), output line explains the missing key. **Do not store a real key to run it live in this task** — the user runs it when they want a live check (same policy as the other live evals).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "test(stt): opt-in Deepgram live eval over WAV fixture"
```

---

### Task 13: Full-suite verification

- [ ] **Step 1: Run everything**

```powershell
dotnet build
dotnet test tests/AIHelperNET.Domain.Tests
dotnet test tests/AIHelperNET.Application.Tests
dotnet test tests/AIHelperNET.Infrastructure.Tests
dotnet test tests/AIHelperNET.App.Tests
dotnet test tests/AIHelperNET.Integration.Tests --filter "Category!=LiveLlm&Category!=LiveStt"
```

Expected: all green except the known pre-existing failures (`RealAudioE2ETests.Scenario4`; possibly `ScriptedInterviewE2ETests.Scenario1` under parallel load — rerun isolated). UITests: run only if touched files affect them (Settings XAML changed → run `dotnet test tests/AIHelperNET.UITests --filter "FullyQualifiedName~Settings"` and remember they use the REAL `D:\AIHelperNET\settings.json` — check `activeBackend` pollution afterwards).

- [ ] **Step 2: Manual smoke (optional but recommended)** — launch via the `run-aihelper` skill, open Settings → Audio, verify the provider radios + key panel toggle; leave provider on Whisper.

- [ ] **Step 3: Commit any stragglers, then hand off to review**

Per repo convention: 1 combined Sonnet review agent over the full branch diff, fix findings, then PR to `develop`.
