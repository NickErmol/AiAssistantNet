namespace AIHelperNET.Application.Answers;

/// <summary>
/// Resolves the language an answer should be written in from the selected Whisper
/// transcription language, so the answer follows the language the interview is spoken in.
/// </summary>
public static class AnswerLanguageResolver
{
    // Whisper language codes (see SettingsWindow.xaml) → human-readable language name used in
    // the answer prompt's "Answer in: {language}." instruction. Codes not listed here (including
    // the "auto" auto-detect sentinel) fall back to the caller-supplied default language.
    private static readonly Dictionary<string, string> CodeToName =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"] = "English",
            ["ru"] = "Russian",
            ["pl"] = "Polish",
        };

    /// <summary>
    /// Returns the answer output language for the given Whisper language code. A specific
    /// language (e.g. <c>ru</c>) overrides <paramref name="fallback"/>; <c>auto</c>, empty,
    /// or an unrecognized code keeps <paramref name="fallback"/> (the configured Output Language).
    /// </summary>
    /// <param name="whisperLanguage">The selected Whisper language code (e.g. <c>auto</c>, <c>en</c>, <c>ru</c>).</param>
    /// <param name="fallback">The configured answer Output Language to use when transcription is auto-detected.</param>
    public static string Resolve(string? whisperLanguage, string fallback)
        => !string.IsNullOrWhiteSpace(whisperLanguage)
           && CodeToName.TryGetValue(whisperLanguage, out var name)
            ? name
            : fallback;
}
