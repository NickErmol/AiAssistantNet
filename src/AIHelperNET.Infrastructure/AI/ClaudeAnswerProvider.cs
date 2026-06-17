using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using Microsoft.Extensions.Options;

namespace AIHelperNET.Infrastructure.AI;

public sealed class ClaudeAnswerProvider(
    HttpClient http,
    ISecretStore secrets,
    IOptions<ClaudeOptions> options) : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Claude;

    /// <summary>
    /// Primes DNS/TLS/connection to the Anthropic endpoint so the first answer doesn't pay the
    /// handshake. Sends a header-only GET (no API key needed — any response, even 401, establishes
    /// the pooled connection that the subsequent POST reuses). Best-effort: never throws.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken ct)
    {
        try
        {
            var opts = options.Value;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{opts.BaseUrl}/v1/models");
            request.Headers.Add("anthropic-version", opts.Version);
            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
#pragma warning disable CA1031 // best-effort warm-up: offline / no network must not surface
        catch (Exception)
        {
            // Intentionally swallowed — the real answer call will pay the handshake if this failed.
        }
#pragma warning restore CA1031
    }

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var keyResult = secrets.GetApiKey();
        if (keyResult.IsFailed)
            throw new InvalidOperationException("No Claude API key configured.");

        var opts = options.Value;
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"{opts.BaseUrl}/v1/messages");

        var apiKey = SecureStringToString(keyResult.Value);
        try
        {
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", opts.Version);
            request.Headers.Add("Accept", "text/event-stream");

            var model = prompt.Model is { } selected ? ClaudeModels.Resolve(selected) : opts.Model;
            var body = ClaudeSse.BuildRequestJson(
                model, prompt.System, prompt.User, prompt.MaxTokens);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException(
                    $"{(int)response.StatusCode} {response.ReasonPhrase}: {errorBody}",
                    null, response.StatusCode);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);

            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var json = line["data:".Length..].Trim();
                if (json is "" or "[DONE]") continue;
                var delta = ClaudeSse.ParseTextDelta(json);
                if (!string.IsNullOrEmpty(delta)) yield return delta;
            }
        }
        finally
        {
            if (apiKey.Length > 0)
            {
                unsafe
                {
                    fixed (char* p = apiKey)
                        for (int i = 0; i < apiKey.Length; i++) p[i] = '\0';
                }
            }
        }
    }

    private static string SecureStringToString(SecureString ss)
    {
        var ptr = Marshal.SecureStringToBSTR(ss);
        try { return Marshal.PtrToStringBSTR(ptr) ?? string.Empty; }
        finally { Marshal.ZeroFreeBSTR(ptr); }
    }
}
