using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text.Json;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.E2E;

/// <summary>
/// Gated live smoke: runs the REAL Ollama answer provider end to end and asserts a non-empty,
/// well-formed answer comes back. Skips (logs + returns) when Ollama is unreachable or no model
/// is pulled, so CI and offline runs stay green without a new test dependency.
/// LiveLlm-tagged → excluded from fast runs.
/// </summary>
[Trait("Category", "LiveLlm")]
public class OllamaLiveAnswerTests(ITestOutputHelper output)
{
    private const string OllamaBaseUrl = "http://localhost:11434";

    /// <summary>
    /// Returns (reachable, firstModelName) — reachable is true only when the endpoint responds
    /// AND at least one model is available via GET /api/tags. When false, firstModelName is null.
    /// </summary>
    private static async Task<(bool reachable, string? firstModel)> ProbeOllamaAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

            // 1. Basic connectivity check.
            var ping = await http.GetAsync(OllamaBaseUrl);
            if (!ping.IsSuccessStatusCode)
                return (false, null);

            // 2. Model-availability check — only proceed when at least one model is pulled.
            var tagsJson = await http.GetStringAsync($"{OllamaBaseUrl}/api/tags");
            using var doc = JsonDocument.Parse(tagsJson);
            var models = doc.RootElement.GetProperty("models");
            if (models.GetArrayLength() == 0)
                return (false, null);

            var firstName = models[0].GetProperty("name").GetString();
            return (true, firstName);
        }
        catch
        {
            return (false, null);
        }
    }

    [Fact]
    public async Task RealOllama_AnswersQuestion_WithNonEmptyCard()
    {
        var (reachable, modelName) = await ProbeOllamaAsync();
        if (!reachable)
        {
            output.WriteLine(
                $"OllamaLiveAnswerTests skipped: {OllamaBaseUrl} unreachable or no models pulled.");
            return; // gated skip — no Ollama available
        }

        output.WriteLine($"Ollama reachable. First available model: {modelName}. Running live assertion.");

        await using var host = await InterviewHost.CreateAsync(useRealAnswerProvider: true);
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
