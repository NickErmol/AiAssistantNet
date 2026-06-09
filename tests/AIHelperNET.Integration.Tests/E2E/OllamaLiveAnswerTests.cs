using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Infrastructure.AI;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Gated live smoke: runs the REAL Ollama answer provider end to end and asserts a non-empty,
/// well-formed answer comes back. Skips (logs + returns) when Ollama is unreachable, or when
/// the model configured in OllamaOptions is not pulled, so CI and offline runs stay green.
/// LiveLlm-tagged → excluded from fast runs.
/// </summary>
[Trait("Category", "LiveLlm")]
public class OllamaLiveAnswerTests(ITestOutputHelper output)
{
    private const string OllamaBaseUrl = "http://localhost:11434";

    /// <summary>
    /// Returns true when GET / at the Ollama root endpoint responds with 200.
    /// Any failure (connection refused, timeout, non-2xx) returns false.
    /// </summary>
    private static async Task<bool> IsOllamaReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await http.GetAsync(OllamaBaseUrl);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="modelName"/> appears in GET /api/tags.
    /// Comparison is case-insensitive on the full name (including :tag suffix).
    /// Any failure parsing the response returns false (never throws).
    /// </summary>
    private static async Task<bool> IsModelPulledAsync(string modelName)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var tagsJson = await http.GetStringAsync($"{OllamaBaseUrl}/api/tags");
            using var doc = JsonDocument.Parse(tagsJson);
            var models = doc.RootElement.GetProperty("models");
            for (int i = 0; i < models.GetArrayLength(); i++)
            {
                var name = models[i].GetProperty("name").GetString();
                if (string.Equals(name, modelName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task RealOllama_AnswersQuestion_WithNonEmptyCard()
    {
        // 1. Cheap endpoint-reachability probe — skip immediately when Ollama is down.
        if (!await IsOllamaReachableAsync())
        {
            output.WriteLine($"OllamaLiveAnswerTests skipped: {OllamaBaseUrl} unreachable.");
            return;
        }

        // 2. Build the host so we can read the *actually configured* model name.
        await using var host = await InterviewHost.CreateAsync(useRealAnswerProvider: true);
        var configuredModel = host.Services
            .GetRequiredService<IOptions<OllamaOptions>>()
            .Value.Model;

        // 3. Confirm the configured model is pulled — skip with a clear reason if not.
        if (!await IsModelPulledAsync(configuredModel))
        {
            output.WriteLine(
                $"OllamaLiveAnswerTests skipped: Ollama up but configured model '{configuredModel}' not pulled.");
            return;
        }

        output.WriteLine($"Ollama reachable and model '{configuredModel}' is available. Running live assertion.");

        // 4. Stream a real answer and assert structure.
        var resolver = host.Services.GetRequiredService<IAnswerProviderResolver>();
        var provider = resolver.Resolve(AiBackend.Ollama);

        var prompt = new AnswerPrompt(
            System: "You are a senior software engineer in a technical interview.",
            User: "In two sentences, what is dependency injection?",
            OutputLanguage: "en",
            MaxTokens: 256);

        var sb = new System.Text.StringBuilder();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await foreach (var chunk in provider.StreamAnswerAsync(prompt, cts.Token))
            sb.Append(chunk);

        var answer = sb.ToString().Trim();
        output.WriteLine($"Answer ({answer.Length} chars): {answer[..Math.Min(200, answer.Length)]}...");

        answer.Should().NotBeNullOrWhiteSpace("the real model should return a non-empty answer");
        answer.Length.Should().BeGreaterThan(20);
    }
}
