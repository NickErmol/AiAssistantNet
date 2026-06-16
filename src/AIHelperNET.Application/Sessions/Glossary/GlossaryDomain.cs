namespace AIHelperNET.Application.Sessions.Glossary;

/// <summary>A named set of transcription-bias terms for one technology domain.</summary>
/// <param name="Key">Stable lowercase identifier (e.g. "dotnet"). Used in settings.</param>
/// <param name="DisplayName">Human-readable name for the Settings UI.</param>
/// <param name="Core">Always-injected high-value terms when the domain is enabled.</param>
/// <param name="Terms">Topic-adaptive candidate terms.</param>
public sealed record GlossaryDomain(
    string Key,
    string DisplayName,
    IReadOnlyList<string> Core,
    IReadOnlyList<string> Terms);
