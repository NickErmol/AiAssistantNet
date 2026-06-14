namespace AIHelperNET.Domain.Questions;

/// <summary>
/// Single source of truth for the lexical data used to detect imperative/command-style
/// requests. Shared by <see cref="QuestionBoundaryDetector"/> and <see cref="QuestionDetector"/>
/// so the two cannot drift apart.
/// </summary>
internal static class QuestionLexicon
{
    /// <summary>
    /// Imperative command verbs that, at the start of a segment, mark an answerable task.
    /// </summary>
    internal static readonly HashSet<string> ImperativeVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        // pre-existing
        "explain", "describe", "write", "implement", "design", "compare",
        "optimize", "refactor", "debug", "walk", "tell", "give", "show",
        "analyze", "fix", "build", "create", "outline", "discuss",
        // added for imperative-question-starters
        "provide", "list", "name", "define", "clarify", "summarize",
        "translate", "convert", "rewrite", "find", "generate", "review",
    };

    /// <summary>
    /// Multi-word imperative starters whose leading token is not a safe standalone verb
    /// (e.g. "break"). Matched as a whole-phrase prefix.
    /// </summary>
    internal static readonly string[] ImperativePhrases = ["break down"];

    /// <summary>
    /// Leading politeness markers stripped before first-word classification, longest first
    /// so "could you please" is consumed before "could you".
    /// </summary>
    private static readonly string[] PolitenessPrefixes =
    [
        "could you please", "can you please", "would you please",
        "could you", "can you", "would you", "please", "kindly",
    ];

    /// <summary>
    /// Removes a single leading politeness prefix (longest match first) and returns the
    /// remaining text trimmed. Returns the input unchanged when no prefix matches.
    /// </summary>
    internal static string StripPoliteness(string text)
    {
        var trimmed = text.TrimStart();
        foreach (var prefix in PolitenessPrefixes)
        {
            if (trimmed.Length > prefix.Length
                && trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                && IsBoundary(trimmed[prefix.Length]))
            {
                return trimmed[prefix.Length..].TrimStart(' ', ',');
            }
        }
        return text;
    }

    /// <summary>
    /// True when <paramref name="text"/>, after politeness stripping, begins with a known
    /// imperative verb or multi-word imperative phrase.
    /// </summary>
    internal static bool StartsWithImperative(string text)
    {
        var stripped = StripPoliteness(text);
        if (ImperativeVerbs.Contains(FirstWord(stripped)))
            return true;
        foreach (var phrase in ImperativePhrases)
        {
            if (stripped.StartsWith(phrase + " ", StringComparison.OrdinalIgnoreCase)
                || stripped.Equals(phrase, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>The word count of <paramref name="text"/> after politeness stripping.</summary>
    internal static int StrippedWordCount(string text) =>
        StripPoliteness(text).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static bool IsBoundary(char c) => c is ' ' or ',';

    private static string FirstWord(string text)
    {
        var idx = text.IndexOf(' ');
        return (idx < 0 ? text : text[..idx]).Trim('?', '.', ',', '!');
    }
}
