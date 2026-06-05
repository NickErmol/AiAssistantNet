using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>
/// Classifies speech segments as new questions, continuations, or non-questions
/// using Claude Haiku via the Anthropic Messages API.
/// </summary>
public sealed class HaikuQuestionClassifier(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IQuestionClassifier
{
    private const string HaikuModel = "claude-haiku-4-5-20251001";

    private const string SystemPrompt =
        "You are classifying speech segments from a live technical interview. " +
        "Reply with exactly one word — no punctuation, no explanation: " +
        "NewQuestion if this is a new interview question, " +
        "Continuation if it continues or completes the previous question, " +
        "NotAQuestion if it is not a question at all.";

    /// <inheritdoc/>
    public async Task<ClassificationResult> ClassifyAsync(
        string combinedText,
        IReadOnlyList<string> recentQuestions,
        CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
        {
            Log.Warning("HaikuClassifier: no API key configured, returning NotAQuestion");
            return ClassificationResult.NotAQuestion;
        }

        var opts = options.Value;
        var contextPart = recentQuestions.Count > 0
            ? "\nRecent questions for context: " + string.Join("; ", recentQuestions)
            : string.Empty;

        var body = JsonSerializer.Serialize(new
        {
            model = HaikuModel,
            max_tokens = 10,
            stream = false,
            system = SystemPrompt + contextPart,
            messages = new[] { new { role = "user", content = combinedText } }
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");

        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning("HaikuClassifier: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return ClassificationResult.NotAQuestion;
            }

            return ParseResponse(json);
        }
        finally
        {
            // SecureStringToString already zeroes via ZeroFreeBSTR;
            // the managed string copy is GC-collected naturally.
            _ = apiKey.Length; // suppress unused-variable warning
        }
    }

    private static ClassificationResult ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var text = doc.RootElement
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString()
                ?.Trim() ?? string.Empty;

            Log.Debug("HaikuClassifier: response = {Text}", text);

            return text switch
            {
                "NewQuestion"  => ClassificationResult.NewQuestion,
                "Continuation" => ClassificationResult.Continuation,
                _              => ClassificationResult.NotAQuestion,
            };
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "HaikuClassifier: failed to parse response");
            return ClassificationResult.NotAQuestion;
        }
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
