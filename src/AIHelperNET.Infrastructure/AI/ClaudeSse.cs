using System.Text.Json;

namespace AIHelperNET.Infrastructure.AI;

public static class ClaudeSse
{
    public static string? ParseTextDelta(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var typeEl)) return null;
            if (typeEl.GetString() != "content_block_delta") return null;

            if (!root.TryGetProperty("delta", out var delta)) return null;
            if (!delta.TryGetProperty("type", out var deltaType)) return null;
            if (deltaType.GetString() != "text_delta") return null;

            return delta.TryGetProperty("text", out var text) ? text.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string BuildRequestJson(
        string model, string systemPrompt, string userPrompt, int maxTokens)
    {
        return JsonSerializer.Serialize(new
        {
            model,
            max_tokens = maxTokens,
            stream = true,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userPrompt } }
        });
    }
}
