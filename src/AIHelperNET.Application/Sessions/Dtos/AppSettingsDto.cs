using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.ValueObjects;

namespace AIHelperNET.Application.Sessions.Dtos;

/// <summary>Serialisable application settings shared between the UI and the settings store.</summary>
/// <param name="ActiveBackend">Selected AI backend.</param>
/// <param name="WhisperModel">Whisper model size used for transcription.</param>
/// <param name="AnswerSettings">Answer style settings.</param>
/// <param name="CodeProfile">Active tech-stack profile injected into prompts.</param>
/// <param name="MicDeviceId">NAudio device ID for the microphone input, or null for the system default.</param>
/// <param name="LoopbackDeviceId">NAudio device ID for loopback capture, or null for the system default.</param>
/// <param name="AnswerFontSize">Font size in points for the answer display area.</param>
/// <param name="WhisperLanguage">Whisper transcription language code, or "auto" for auto-detection.</param>
/// <param name="OverlayOpacity">Overlay window opacity in range [0.2, 1.0].</param>
/// <param name="MaxAnswerTokens">Maximum tokens for a generated audio answer, in range [200, 4000].</param>
/// <param name="LatestQuestionWindowSeconds">Look-back window (seconds) the Answer-latest-question
/// hotkey scans for the most recent question, in range [30, 300].</param>
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
    int MaxAnswerTokens = 800,
    int LatestQuestionWindowSeconds = 120)
{
    /// <summary>Default answer-token cap used when unset/legacy.</summary>
    public const int DefaultMaxAnswerTokens = 800;
    /// <summary>Minimum allowed answer-token cap.</summary>
    public const int MinAnswerTokens = 200;
    /// <summary>Maximum allowed answer-token cap.</summary>
    public const int MaxAnswerTokensLimit = 4000;

    /// <summary>Default Answer-latest-question look-back window, in seconds.</summary>
    public const int DefaultLatestQuestionWindowSeconds = 120;
    /// <summary>Minimum Answer-latest-question look-back window, in seconds.</summary>
    public const int MinLatestQuestionWindowSeconds = 30;
    /// <summary>Maximum Answer-latest-question look-back window, in seconds.</summary>
    public const int MaxLatestQuestionWindowSeconds = 300;

    /// <summary>Named setting presets for quick profile switching.</summary>
    public IReadOnlyList<ProfilePreset> Presets { get; init; } = [];

    /// <summary>User overrides of the default global-hotkey chords. Empty ⇒ all defaults.</summary>
    public IReadOnlyList<HotkeyOverride> HotkeyOverrides { get; init; } = [];

    /// <summary>Returns a copy with <see cref="MaxAnswerTokens"/> and <see cref="LatestQuestionWindowSeconds"/>
    /// coerced into their valid ranges: missing/non-positive → defaults; otherwise clamped.
    /// Also strips invalid or duplicate-id entries from <see cref="HotkeyOverrides"/>.</summary>
    public AppSettingsDto Normalized() => this with
    {
        MaxAnswerTokens = MaxAnswerTokens <= 0
            ? DefaultMaxAnswerTokens
            : Math.Clamp(MaxAnswerTokens, MinAnswerTokens, MaxAnswerTokensLimit),
        LatestQuestionWindowSeconds = LatestQuestionWindowSeconds <= 0
            ? DefaultLatestQuestionWindowSeconds
            : Math.Clamp(LatestQuestionWindowSeconds, MinLatestQuestionWindowSeconds, MaxLatestQuestionWindowSeconds),
        HotkeyOverrides = NormalizeOverrides(HotkeyOverrides)
    };

    private static IReadOnlyList<HotkeyOverride> NormalizeOverrides(IReadOnlyList<HotkeyOverride> raw)
    {
        if (raw is null or { Count: 0 }) return [];

        const uint modMask = (uint)(ModifierKeys.Alt | ModifierKeys.Ctrl | ModifierKeys.Shift | ModifierKeys.Win);
        var seen = new HashSet<HotkeyId>();
        var result = new List<HotkeyOverride>(raw.Count);
        foreach (var o in raw)
        {
            if (!Enum.IsDefined(o.Id)) continue;              // unknown action
            if (((uint)o.Modifiers & ~modMask) != 0) continue; // stray modifier bits
            if (o.Modifiers == ModifierKeys.None) continue;   // a global hotkey needs at least one modifier
            if (!Enum.IsDefined(o.Key)) continue;             // unknown key
            if (!seen.Add(o.Id)) continue;                    // keep first per action
            result.Add(o);
        }

        // Return the original list unchanged when nothing was filtered — preserves reference
        // equality so that record structural comparison in tests stays stable.
        return result.Count == raw.Count ? raw : result;
    }
}
