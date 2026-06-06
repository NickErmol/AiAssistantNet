using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using Microsoft.Extensions.Options;
using Serilog;

namespace AIHelperNET.Infrastructure.AI;

/// <summary>
/// Classifies transcript boundary context using Claude Haiku via the Anthropic Messages API,
/// returning a structured <see cref="BoundaryClassificationResult"/>.
/// </summary>
public sealed class QuestionBoundaryClassifier(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IQuestionBoundaryClassifier
{
    private const string HaikuModel = "claude-haiku-4-5-20251001";

    private const string SystemPrompt =
        "You are classifying a live technical interview speech segment for boundary detection.\n" +
        "You must return valid JSON only — no prose, no markdown, no code blocks.\n" +
        "Valid labels: NoQuestion | QuestionStarted | QuestionContinued | QuestionComplete | TaskComplete | ClarificationOfCurrentQuestion | AdditionalRequirement | NewQuestion | Unrelated\n" +
        "JSON schema: {\"classification\":\"<label>\",\"confidence\":<0.0-1.0>,\"normalized_text\":\"<trimmed input>\",\"reason\":\"<one short sentence>\"}";

    /// <inheritdoc/>
    public async Task<BoundaryClassificationResult> ClassifyAsync(
        ConversationTurnStatus? activeTurnStatus,
        IReadOnlyList<TranscriptItem> recentItems,
        TranscriptItem latestItem,
        Speaker speaker,
        CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
        {
            Log.Warning("QuestionBoundaryClassifier: no API key configured, returning Ambiguous");
            return BoundaryClassificationResult.Ambiguous(latestItem.Text);
        }

        var opts = options.Value;

        var activeTurnStatusStr = activeTurnStatus?.ToString() ?? "null";
        var recentItemsList = recentItems
            .TakeLast(5)
            .Select(item => new { speaker = item.Speaker.ToString(), text = item.Text })
            .ToList();
        var textToClassify = latestItem.Text;

        var userMessage = JsonSerializer.Serialize(new
        {
            active_turn_status = activeTurnStatusStr,
            recent_items = recentItemsList,
            text_to_classify = textToClassify
        });

        var body = JsonSerializer.Serialize(new
        {
            model = HaikuModel,
            max_tokens = 200,
            stream = false,
            system = SystemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
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
                Log.Warning("QuestionBoundaryClassifier: API error {Status} — {Body}",
                    (int)response.StatusCode, json[..Math.Min(200, json.Length)]);
                return BoundaryClassificationResult.Ambiguous(latestItem.Text);
            }

            return ParseResponse(json, latestItem.Text);
        }
        finally
        {
            // SecureStringToString already zeroes via ZeroFreeBSTR;
            // the managed string copy is GC-collected naturally.
            _ = apiKey.Length; // suppress unused-variable warning
        }
    }

    private static BoundaryClassificationResult ParseResponse(string json, string originalText)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var content = root.GetProperty("content")[0].GetProperty("text").GetString()?.Trim() ?? "";
            using var resultDoc = JsonDocument.Parse(content);
            var r = resultDoc.RootElement;
            var label = ParseLabel(r.GetProperty("classification").GetString() ?? "");
            var confidence = r.GetProperty("confidence").GetDouble();
            var normalizedText = r.TryGetProperty("normalized_text", out var nt) ? nt.GetString() ?? originalText : originalText;
            var reason = r.TryGetProperty("reason", out var rs) ? rs.GetString() ?? "" : "";
            return new BoundaryClassificationResult(
                label, confidence,
                label is BoundaryLabel.QuestionComplete or BoundaryLabel.TaskComplete or BoundaryLabel.NewQuestion,
                label is BoundaryLabel.AdditionalRequirement or BoundaryLabel.ClarificationOfCurrentQuestion,
                label is BoundaryLabel.QuestionStarted or BoundaryLabel.QuestionComplete or BoundaryLabel.TaskComplete or BoundaryLabel.NewQuestion,
                normalizedText, reason);
        }
        catch
        {
            return BoundaryClassificationResult.Ambiguous(originalText);
        }
    }

    private static BoundaryLabel ParseLabel(string s) => s switch
    {
        "NoQuestion"                     => BoundaryLabel.NoQuestion,
        "QuestionStarted"                => BoundaryLabel.QuestionStarted,
        "QuestionContinued"              => BoundaryLabel.QuestionContinued,
        "QuestionComplete"               => BoundaryLabel.QuestionComplete,
        "TaskComplete"                   => BoundaryLabel.TaskComplete,
        "ClarificationOfCurrentQuestion" => BoundaryLabel.ClarificationOfCurrentQuestion,
        "AdditionalRequirement"          => BoundaryLabel.AdditionalRequirement,
        "NewQuestion"                    => BoundaryLabel.NewQuestion,
        "Unrelated"                      => BoundaryLabel.Unrelated,
        _                                => BoundaryLabel.NoQuestion
    };

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
