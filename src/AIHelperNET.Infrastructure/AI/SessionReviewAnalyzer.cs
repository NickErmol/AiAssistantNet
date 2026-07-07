using System.Net.Http;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Infrastructure.Security;
using FluentResults;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>
/// Sends the assembled review <see cref="AnswerPrompt"/> to the Claude Messages API (non-streaming)
/// and returns the generated markdown report as a <see cref="SessionReviewResult"/>.
/// Uses Sonnet (resolved via <see cref="ClaudeModels.Resolve"/>) and a 5-minute HTTP timeout
/// to accommodate 8 k-token generation.
/// </summary>
public sealed class SessionReviewAnalyzer(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : ISessionReviewAnalyzer
{
    /// <inheritdoc/>
    public async Task<Result<SessionReviewResult>> AnalyzeAsync(
        AnswerPrompt prompt, CancellationToken ct)
    {
        // 1. Resolve API key — fail fast without touching the network.
        var keyResult = secrets.GetApiKey(SecretKind.Anthropic);
        if (keyResult.IsFailed)
        {
            Log.Warning("SessionReviewAnalyzer: no API key configured");
            return Result.Fail(
                "No Claude API key configured — add one in Settings.");
        }

        var opts = options.Value;
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
        var apiKey = SecureStringHelpers.ConvertToString(keyResult.Value);
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
                Log.Warning("SessionReviewAnalyzer: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return Result.Fail(
                    $"Claude API returned {(int)response.StatusCode} — review generation failed.");
            }

            return ParseResult(json, resolvedModel);
        }
        finally
        {
            SecureStringHelpers.ZeroString(apiKey);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static Result<SessionReviewResult> ParseResult(string json, string modelUsed)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var contentArray = doc.RootElement.GetProperty("content");

            if (contentArray.GetArrayLength() == 0)
            {
                Log.Warning("SessionReviewAnalyzer: response content array is empty");
                return Result.Fail("Claude returned an empty response — review generation failed.");
            }

            var text = contentArray[0].GetProperty("text").GetString()?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(text))
            {
                Log.Warning("SessionReviewAnalyzer: response text is empty");
                return Result.Fail("Claude returned empty text — review generation failed.");
            }

            Log.Debug("SessionReviewAnalyzer: review generated ({Chars} chars)", text.Length);
            return Result.Ok(new SessionReviewResult(text, modelUsed));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "SessionReviewAnalyzer: failed to parse Claude response");
            return Result.Fail("Could not parse Claude response — review generation failed.");
        }
    }

}
