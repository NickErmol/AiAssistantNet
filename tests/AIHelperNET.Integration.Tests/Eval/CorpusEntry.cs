using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>One transcript item in a corpus entry's recent-context window.</summary>
public sealed record CorpusItem(Speaker Speaker, string Text);

/// <summary>A single labeled classification case: inputs + the human-assigned correct label.</summary>
public sealed record CorpusEntry(
    string Id,
    IReadOnlyList<CorpusItem> RecentItems,
    CorpusItem LatestItem,
    ConversationTurnStatus? ActiveTurnStatus,
    BoundaryLabel ExpectedLabel,
    string? Note);

/// <summary>Loads the checked-in boundary corpus from the test output directory.</summary>
public static class CorpusLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Path to the corpus JSON copied next to the test assembly.</summary>
    public static string CorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Eval", "boundary-corpus.json");

    /// <summary>Deserializes the corpus. Throws if the file is missing, malformed, or empty.</summary>
    public static IReadOnlyList<CorpusEntry> Load()
    {
        var json = File.ReadAllText(CorpusPath);
        var entries = JsonSerializer.Deserialize<List<CorpusEntry>>(json, Options)
            ?? throw new InvalidOperationException("Corpus deserialized to null.");
        if (entries.Count == 0)
            throw new InvalidOperationException("Corpus is empty.");
        return entries;
    }
}
