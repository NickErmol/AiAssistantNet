namespace AIHelperNET.Application.Sessions.Glossary;

/// <summary>Pure, deterministic selection of transcription-bias terms under a word budget.</summary>
public static class GlossarySelector
{
    /// <summary>Score added to core terms so they are generally preferred over unrelated candidate
    /// terms, while still allowing high-relevance candidates to rank above weakly-relevant core terms.</summary>
    private const int CoreBonus = 3;

    /// <summary>Selects terms from the enabled domains, prioritising core terms then terms most
    /// lexically relevant to <paramref name="recentContext"/>, never exceeding <paramref name="wordBudget"/> words.</summary>
    /// <param name="domains">All known glossary domains.</param>
    /// <param name="enabledKeys">Keys of the domains the user has enabled.</param>
    /// <param name="recentContext">Recently transcribed text used to bias term relevance; may be empty.</param>
    /// <param name="wordBudget">Maximum total words across the returned terms.</param>
    /// <returns>Ordered, de-duplicated terms: highest-scored first, within budget.</returns>
    public static IReadOnlyList<string> Select(
        IReadOnlyList<GlossaryDomain> domains,
        IReadOnlySet<string> enabledKeys,
        string recentContext,
        int wordBudget)
    {
        if (domains.Count == 0 || enabledKeys.Count == 0 || wordBudget <= 0)
            return [];

        var enabled = domains.Where(d => enabledKeys.Contains(d.Key)).ToList();
        if (enabled.Count == 0) return [];

        var contextTokens = Tokenize(recentContext);

        // Build a unified ranked list: core terms get a bonus so they generally come first,
        // but a strongly context-relevant candidate term can still outrank a weakly-relevant core term.
        var allCandidates = enabled
            .SelectMany(d =>
                d.Core.Select(t => (Term: t, IsCore: true))
                .Concat(d.Terms.Select(t => (Term: t, IsCore: false))))
            .Select((x, globalOrder) => (
                x.Term,
                Score: Relevance(x.Term, contextTokens, recentContext) + (x.IsCore ? CoreBonus : 0),
                GlobalOrder: globalOrder))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.GlobalOrder);

        var selected = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int usedWords = 0;

        foreach (var x in allCandidates)
        {
            if (seen.Contains(x.Term)) continue;
            int w = WordCount(x.Term);
            if (usedWords + w > wordBudget)
            {
                if (usedWords >= wordBudget) break;
                continue; // skip this term, try a shorter one
            }
            seen.Add(x.Term);
            selected.Add(x.Term);
            usedWords += w;
        }

        return selected;
    }

    private static int Relevance(string term, HashSet<string> contextTokens, string recentContext)
    {
        if (contextTokens.Count == 0) return 0;
        int score = 0;
        foreach (var tok in Tokenize(term))
            if (contextTokens.Contains(tok)) score += 2;
        if (recentContext.Contains(term, StringComparison.OrdinalIgnoreCase)) score += 5;
        return score;
    }

    private static int WordCount(string text) =>
        text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static HashSet<string> Tokenize(string text) =>
        [.. text.ToLowerInvariant()
            .Split([' ', '.', ',', '?', '!', ';', ':', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 2)];
}
