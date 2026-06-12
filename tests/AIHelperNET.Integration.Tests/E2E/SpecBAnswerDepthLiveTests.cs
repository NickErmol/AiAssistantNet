using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Security;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Spec B manual-verification harness (live Claude). Builds the PRODUCTION audio prompt via
/// <see cref="PromptBuilderService.Build(CodeProfile, AnswerSettings, string, string?, IReadOnlyList{Domain.Sessions.TranscriptItem}?, IReadOnlyList{ValueTuple{string,string}}?, int?)"/>
/// at the Spec B default 800-token cap, then sends it to the real Anthropic API using the key from
/// Windows Credential Manager (same store production uses). Gated/self-skips when no key is present,
/// so CI and offline runs stay green. Uses a non-streaming request so it can read back
/// <c>stop_reason</c> and <c>usage.output_tokens</c> — the deterministic proof of "no truncation".
/// LiveLlm-tagged → excluded from fast runs.
/// </summary>
[Trait("Category", "LiveLlm")]
public class SpecBAnswerDepthLiveTests(ITestOutputHelper output)
{
    private const int SpecBDefaultCap = 800; // AppSettingsDto.DefaultMaxAnswerTokens

    [Fact]
    public async Task TrivialAndHard_AtEightHundredCap_DepthScalesAndNoTruncation()
    {
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey())
        {
            output.WriteLine("Skipped: no Claude API key in Windows Credential Manager " +
                "(target 'AIHelperNET:ClaudeApiKey').");
            return;
        }

        var opts = new ClaudeOptions();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var apiKey = SecureToString(secrets.GetApiKey().Value);

        // (a) Trivial/factual question — difficulty instruction should keep it to 1–2 sentences.
        var trivial = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is a primary key in SQL?",
            maxTokens: SpecBDefaultCap);

        // (b) Hard design/trade-off question — should complete WITHIN the 800 cap (stop_reason
        // 'end_turn', not 'max_tokens'); this is the case that previously truncated mid-word at 300.
        var hard = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "How would you design a rate limiter for a distributed API serving millions of " +
            "requests per second, and what are the trade-offs between the approaches?",
            maxTokens: SpecBDefaultCap);

        var trivialResult = await CallAsync(http, opts, apiKey, trivial);
        var hardResult = await CallAsync(http, opts, apiKey, hard);

        Report("(a) TRIVIAL — 'What is a primary key in SQL?'", trivialResult);
        Report("(b) HARD — distributed rate limiter design", hardResult);

        // --- (b) deterministic: the cap must not cut the answer off mid-stream. ---
        hardResult.StopReason.Should().NotBe("max_tokens",
            "Spec B raised the default cap to 800 so a hard answer completes instead of being " +
            $"truncated mid-word; got stop_reason='{hardResult.StopReason}', " +
            $"output_tokens={hardResult.OutputTokens}/{SpecBDefaultCap}.");
        hardResult.Text.Trim().Should().NotBeNullOrWhiteSpace();
        EndsCleanly(hardResult.Text).Should().BeTrue(
            $"a completed answer should end on terminal punctuation, not mid-word; " +
            $"tail=\"…{Tail(hardResult.Text)}\"");

        // --- (a) report-only depth signal (model prose is probabilistic; logged for human eyeball). ---
        trivialResult.StopReason.Should().NotBe("max_tokens");
        output.WriteLine(
            $"\nDEPTH SIGNAL: trivial={trivialResult.OutputTokens} tok vs hard={hardResult.OutputTokens} tok " +
            $"(expect trivial materially shorter; trivial should skip the '- ' bullet scaffold).");
    }

    private static async Task<ClaudeResult> CallAsync(
        HttpClient http, ClaudeOptions opts, string apiKey, AnswerPrompt prompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model = opts.Model,
            max_tokens = prompt.MaxTokens,
            stream = false,
            system = prompt.System,
            messages = new[] { new { role = "user", content = prompt.User } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", opts.Version);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await http.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = new StringBuilder();
        foreach (var block in root.GetProperty("content").EnumerateArray())
            if (block.GetProperty("type").GetString() == "text")
                text.Append(block.GetProperty("text").GetString());

        var stop = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
        var outTok = root.GetProperty("usage").GetProperty("output_tokens").GetInt32();
        return new ClaudeResult(text.ToString(), stop, outTok);
    }

    private void Report(string title, ClaudeResult r)
    {
        var bullets = r.Text.Split('\n').Count(l => l.TrimStart().StartsWith("- ", StringComparison.Ordinal));
        output.WriteLine($"===== {title} =====");
        output.WriteLine($"stop_reason={r.StopReason}  output_tokens={r.OutputTokens}  " +
            $"chars={r.Text.Length}  '- ' bullets={bullets}");
        output.WriteLine(r.Text.Trim());
        output.WriteLine("");
    }

    private static bool EndsCleanly(string text)
    {
        var t = text.TrimEnd();
        if (t.Length == 0) return false;
        var last = t[^1];
        return last is '.' or '!' or '?' or ':' or '`' or '*' or ')' or '"';
    }

    private static string Tail(string text)
    {
        var t = text.TrimEnd();
        return t.Length <= 60 ? t : t[^60..];
    }

    private static string SecureToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }

    private sealed record ClaudeResult(string Text, string? StopReason, int OutputTokens);
}
