using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>A judge model's grade for one generated card.</summary>
public sealed record JudgeVerdict(string Verdict, double Score, string Reason);

/// <summary>Pure, deterministic grading helpers for generated answer cards. No I/O.</summary>
public static class ScreenCardGrader
{
    private static readonly JsonSerializerOptions JudgeJson = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>True when the card contains at least one fenced code block.</summary>
    public static bool HasFencedCode(string text) =>
        text.Contains("```", StringComparison.Ordinal);

    /// <summary>Returns the required substrings that are absent (case-insensitive).</summary>
    public static IReadOnlyList<string> MissingSubstrings(string text, IEnumerable<string> required) =>
        required.Where(s => !text.Contains(s, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>Parses a judge response into a <see cref="JudgeVerdict"/>, tolerating a leading
    /// ```json fence (the bug that once silently broke the boundary classifier). Returns a
    /// zero-score "UNPARSEABLE" verdict rather than throwing on malformed output.</summary>
    public static JudgeVerdict ParseJudgeVerdict(string raw)
    {
        var json = StripCodeFence(raw);
        try
        {
            var dto = JsonSerializer.Deserialize<JudgeDto>(json, JudgeJson);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Verdict))
                return new JudgeVerdict("UNPARSEABLE", 0.0, raw.Trim());
            return new JudgeVerdict(dto.Verdict, dto.Score, dto.Reason ?? "");
        }
        catch (JsonException)
        {
            return new JudgeVerdict("UNPARSEABLE", 0.0, raw.Trim());
        }
    }

    private static string StripCodeFence(string text)
    {
        var t = text.Trim();
        if (!t.StartsWith("```", StringComparison.Ordinal)) return t;
        var firstNewline = t.IndexOf('\n');
        if (firstNewline < 0) return t;
        t = t[(firstNewline + 1)..];                       // drop the ```json line
        var lastFence = t.LastIndexOf("```", StringComparison.Ordinal);
        return (lastFence >= 0 ? t[..lastFence] : t).Trim(); // drop the trailing fence
    }

    private sealed record JudgeDto(
        [property: JsonPropertyName("verdict")] string? Verdict,
        [property: JsonPropertyName("score")] double Score,
        [property: JsonPropertyName("reason")] string? Reason);
}
