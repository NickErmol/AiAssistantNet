using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Profile;
using FluentResults;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>
/// Sends the assembled condensation <see cref="AnswerPrompt"/> to the Claude Messages API
/// (non-streaming) and returns the generated profile card text.
/// Uses Sonnet (resolved via <see cref="ClaudeModels.Resolve"/>) and a 2-minute HTTP timeout
/// to accommodate condensation.
/// </summary>
public sealed class ProfileCondenser(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IProfileCondenser
{
    /// <inheritdoc/>
    public async Task<Result<string>> CondenseAsync(
        string resumeText,
        string? jobDescriptionText,
        CancellationToken ct)
    {
        // 1. Resolve API key — fail fast without touching the network.
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
        {
            Log.Warning("ProfileCondenser: no API key configured");
            return Result.Fail(
                "No Claude API key configured — add one in Settings.");
        }

        var opts = options.Value;
        var prompt = CandidateProfilePromptBuilder.Build(resumeText, jobDescriptionText);
        var resolvedModel = ClaudeModels.Resolve(prompt.Model ?? AnswerModel.Sonnet);

        var body = JsonSerializer.Serialize(new
        {
            model = resolvedModel,
            max_tokens = prompt.MaxTokens,
            stream = false,
            system = prompt.System,
            messages = new[] { new { role = "user", content = prompt.User } },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");
        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            // Let OperationCanceledException propagate naturally — don't convert user cancellation
            // into a failure Result (the caller interprets it as explicit user abort).
            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("ProfileCondenser: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return Result.Fail(
                    $"Claude API returned {(int)response.StatusCode} — profile condensation failed.");
            }

            return ParseResult(json);
        }
        finally
        {
            _ = apiKey.Length; // managed copy GC-collected; BSTR already zeroed by SecureStringToString
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static Result<string> ParseResult(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var contentArray = doc.RootElement.GetProperty("content");

            if (contentArray.GetArrayLength() == 0)
            {
                Log.Warning("ProfileCondenser: response content array is empty");
                return Result.Fail("Claude returned an empty response — profile condensation failed.");
            }

            var text = contentArray[0].GetProperty("text").GetString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warning("ProfileCondenser: response text is empty");
                return Result.Fail("Claude returned empty text — profile condensation failed.");
            }

            Log.Debug("ProfileCondenser: condensed profile generated ({Chars} chars)", text.Length);
            return Result.Ok(text);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ProfileCondenser: failed to parse Claude response");
            return Result.Fail("Could not parse Claude response — profile condensation failed.");
        }
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
