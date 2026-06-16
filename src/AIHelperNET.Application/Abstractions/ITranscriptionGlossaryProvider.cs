using AIHelperNET.Application.Sessions.Glossary;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Supplies the transcription glossary and builds a Whisper prompt suffix from it.</summary>
public interface ITranscriptionGlossaryProvider
{
    /// <summary>All known glossary domains (for the Settings UI and selection).</summary>
    IReadOnlyList<GlossaryDomain> Domains { get; }

    /// <summary>Builds a space-joined glossary suffix for the enabled domains, biased toward
    /// <paramref name="recentContext"/>, capped at <paramref name="wordBudget"/> words.
    /// Returns empty string when nothing is selected.</summary>
    /// <param name="enabledKeys">Enabled domain keys.</param>
    /// <param name="recentContext">Recently transcribed text used to bias relevance.</param>
    /// <param name="wordBudget">Maximum total words across selected terms.</param>
    string BuildPromptSuffix(IReadOnlySet<string> enabledKeys, string recentContext, int wordBudget);
}
