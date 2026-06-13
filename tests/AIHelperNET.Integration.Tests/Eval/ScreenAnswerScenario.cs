using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHelperNET.Application.Answers;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Candidate stack for a scenario; null fields are omitted from the prompt.</summary>
public sealed record CorpusProfile(string? Language, string? Backend, string? Frontend);

/// <summary>A delayed interviewer follow-up turn and how to grade the updated card.</summary>
public sealed record FollowUpTurn(
    string Speech,
    string PriorAnswer,
    bool RequireCode,
    IReadOnlyList<string> RequiredSubstrings,
    string ExpectedCriteria);

/// <summary>One live-eval scenario: interviewer speech + captured screen + grading rubric.</summary>
public sealed record ScreenAnswerScenario(
    string Id,
    ScreenAnalysisMode Mode,
    CorpusProfile Profile,
    string InterviewerSpeech,
    string ScreenOcr,
    bool RequireCode,
    IReadOnlyList<string> RequiredSubstrings,
    string ExpectedCriteria,
    FollowUpTurn? FollowUp);

/// <summary>Loads the checked-in screen-answer corpus from the test output directory.</summary>
public static class ScreenAnswerCorpusLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Path to the corpus JSON copied next to the test assembly.</summary>
    public static string CorpusPath =>
        Path.Combine(AppContext.BaseDirectory, "Eval", "screen-answer-corpus.json");

    /// <summary>Deserializes the corpus. Throws if the file is missing, malformed, or empty.</summary>
    public static IReadOnlyList<ScreenAnswerScenario> Load()
    {
        var json = File.ReadAllText(CorpusPath);
        var entries = JsonSerializer.Deserialize<List<ScreenAnswerScenario>>(json, Options)
            ?? throw new InvalidOperationException("screen-answer-corpus.json deserialized to null.");
        if (entries.Count == 0)
            throw new InvalidOperationException("screen-answer-corpus.json is empty.");
        return entries;
    }
}
