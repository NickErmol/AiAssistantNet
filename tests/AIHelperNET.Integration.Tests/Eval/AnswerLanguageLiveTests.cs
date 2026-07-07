using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.Json;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using AIHelperNET.Infrastructure.AI;
using AIHelperNET.Infrastructure.Security;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Opt-in live eval for the "answers follow the transcription language" feature: drives the
/// real code path (<see cref="AnswerLanguageResolver"/> → <see cref="PromptBuilderService"/> →
/// production Claude) and asserts a Russian transcription selection produces a Cyrillic answer while
/// auto-detect stays in the configured English. Self-skips (passes trivially) when no Anthropic key
/// is in Windows Credential Manager, so CI and offline runs stay green.</summary>
[Trait("Category", "LiveLlm")]
public class AnswerLanguageLiveTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task RussianTranscriptionLanguage_ProducesCyrillicAnswer_AutoStaysEnglish()
    {
        var secrets = new WindowsCredentialSecretStore();
        if (!secrets.HasApiKey(SecretKind.Anthropic))
        {
            output.WriteLine("Skipped: no Claude API key in Windows Credential Manager " +
                "(target 'AIHelperNET:ClaudeApiKey').");
            return;
        }

        var opts = new ClaudeOptions();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
        var apiKey = SecureToString(secrets.GetApiKey(SecretKind.Anthropic).Value);

        var profile = CodeProfile.Empty with { ProgrammingLanguage = "C#" };
        var question = DetectedQuestion.Create(
            "What is dependency injection and why is it useful?", QuestionSource.Audio, Now);

        // Russian transcription selected ("ru") → resolver overrides Output Language → Russian answer.
        var ruSettings = AnswerSettings.Default with
        {
            OutputLanguage = AnswerLanguageResolver.Resolve("ru", AnswerSettings.Default.OutputLanguage),
        };
        ruSettings.OutputLanguage.Should().Be("Russian", "resolver maps the 'ru' code to a language name");
        var ruPrompt = PromptBuilderService.Build(profile, ruSettings, question);
        ruPrompt.System.Should().Contain("Answer in: Russian.");
        var ruAnswer = await GenerateAsync(http, opts, apiKey, opts.Model, ruPrompt);

        // Auto-detect ("auto") → resolver keeps the configured Output Language (English).
        var autoSettings = AnswerSettings.Default with
        {
            OutputLanguage = AnswerLanguageResolver.Resolve("auto", AnswerSettings.Default.OutputLanguage),
        };
        autoSettings.OutputLanguage.Should().Be("English");
        var autoPrompt = PromptBuilderService.Build(profile, autoSettings, question);
        var autoAnswer = await GenerateAsync(http, opts, apiKey, opts.Model, autoPrompt);

        output.WriteLine("=== Russian (transcription=ru) ===");
        output.WriteLine(ruAnswer);
        output.WriteLine("\n=== English (transcription=auto) ===");
        output.WriteLine(autoAnswer);

        CyrillicRatio(ruAnswer).Should().BeGreaterThan(0.30,
            "a Russian-language answer should be predominantly Cyrillic");
        CyrillicRatio(autoAnswer).Should().BeLessThan(0.05,
            "auto-detect keeps the English Output Language, so the answer should be Latin script");
    }

    /// <summary>Fraction of letters that are Cyrillic (U+0400–U+04FF). Code fences and punctuation are
    /// excluded by only counting letters, so an answer with a small fenced C# snippet still scores high.</summary>
    private static double CyrillicRatio(string text)
    {
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return 0.0;
        var cyrillic = letters.Count(c => c is >= 'Ѐ' and <= 'ӿ');
        return (double)cyrillic / letters.Count;
    }

    private static async Task<string> GenerateAsync(
        HttpClient http, ClaudeOptions opts, string apiKey, string model, AnswerPrompt prompt)
    {
        var body = JsonSerializer.Serialize(new
        {
            model,
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
        var text = new StringBuilder();
        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
            if (block.GetProperty("type").GetString() == "text")
                text.Append(block.GetProperty("text").GetString());
        return text.ToString();
    }

    private static string SecureToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
