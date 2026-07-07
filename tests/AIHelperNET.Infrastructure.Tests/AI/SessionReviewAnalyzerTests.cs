using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Infrastructure.AI;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public sealed class SessionReviewAnalyzerTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static SessionReviewAnalyzer MakeSut(
        string responseBody,
        HttpStatusCode status = HttpStatusCode.OK,
        bool hasApiKey = true)
    {
        var handler = new ReviewMockHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        return BuildSut(http, hasApiKey);
    }

    private static SessionReviewAnalyzer BuildSut(HttpClient http, bool hasApiKey)
    {
        var secrets = Substitute.For<ISecretStore>();
        if (hasApiKey)
        {
            var ss = new SecureString();
            foreach (var c in "fake-review-key") ss.AppendChar(c);
            ss.MakeReadOnly();
            secrets.GetApiKey().Returns(Result.Ok(ss));
        }
        else
        {
            secrets.GetApiKey().Returns(Result.Fail<SecureString>("no key"));
        }

        return new SessionReviewAnalyzer(http, secrets, Options.Create(new ClaudeOptions()));
    }

    /// <summary>
    /// Creates a SUT + handler pair for tests that need to assert on the HTTP request.
    /// The handler is passed back via out param to keep method signatures free of file-local types.
    /// </summary>
    private static SessionReviewAnalyzer MakeSutWithTracker(
        string responseBody,
        out ReviewMockHandler handler,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new ReviewMockHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        return BuildSut(http, hasApiKey: true);
    }

    private static string ApiResponse(string markdownText)
    {
        var escaped = markdownText
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
        return $"{{\"id\":\"msg_review_01\",\"type\":\"message\",\"role\":\"assistant\"," +
               $"\"content\":[{{\"type\":\"text\",\"text\":\"{escaped}\"}}]," +
               $"\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\"," +
               $"\"usage\":{{\"input_tokens\":100,\"output_tokens\":200}}}}";
    }

    private static AnswerPrompt SonnetReviewPrompt() =>
        new(
            System: "You are a technical-interview post-mortem analyst.",
            User: "FULL TRANSCRIPT:\n[00:00] Interviewer: What is polymorphism?",
            OutputLanguage: "English",
            MaxTokens: 8000,
            Model: AnswerModel.Sonnet);

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnalyzeAsync_HappyPath_ReturnsMarkdownAndModelUsed()
    {
        var expectedMarkdown = "## Questions Asked\n- What is polymorphism?";
        var sut = MakeSut(ApiResponse(expectedMarkdown));

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markdown.Should().Be(expectedMarkdown);
        result.Value.ModelUsed.Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public async Task AnalyzeAsync_ModelUsed_IsResolvedSonnetId()
    {
        var sut = MakeSut(ApiResponse("## Questions Asked\n- Q?"));

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.ModelUsed.Should().Be("claude-sonnet-4-6");
    }

    [Fact]
    public async Task AnalyzeAsync_NoApiKey_ReturnsFail()
    {
        var sut = MakeSut("", hasApiKey: false);

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should()
            .Contain("API key");
    }

    [Fact]
    public async Task AnalyzeAsync_NoApiKey_DoesNotCallHttp()
    {
        var callCount = 0;
        var countingHandler = new ReviewCallCountHandler(() =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(countingHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetApiKey().Returns(Result.Fail<SecureString>("no key"));
        var sut = new SessionReviewAnalyzer(http, secrets, Options.Create(new ClaudeOptions()));

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        callCount.Should().Be(0, "HTTP must not be reached when no key is configured");
    }

    [Fact]
    public async Task AnalyzeAsync_Http500_ReturnsFail()
    {
        var sut = MakeSut(
            """{"error":{"type":"server_error","message":"internal"}}""",
            HttpStatusCode.InternalServerError);

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_EmptyContentArray_ReturnsFail()
    {
        const string emptyContent =
            """{"id":"msg_01","type":"message","role":"assistant","content":[],"model":"claude-sonnet-4-6","stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":0}}""";
        var sut = MakeSut(emptyContent);

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_MalformedJson_ReturnsFail()
    {
        var sut = MakeSut("not valid json {{{{");

        var result = await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task AnalyzeAsync_SendsRequestToCorrectEndpoint()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
    }

    [Fact]
    public async Task AnalyzeAsync_SendsApiKeyHeader()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        // CapturedApiKey is a snapshot taken inside SendAsync, before ZeroString runs
        // in the caller's finally block and zeroes the original string buffer in-place.
        handler.CapturedApiKey.Should().Be("fake-review-key");
    }

    [Fact]
    public async Task AnalyzeAsync_SendsAnthropicVersionHeader()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.Headers.Should().ContainKey("anthropic-version");
    }

    [Fact]
    public async Task AnalyzeAsync_RequestBodyContainsCorrectModelId()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        handler.LastRequestBody.Should().NotBeNullOrWhiteSpace();
        handler.LastRequestBody.Should().Contain("\"claude-sonnet-4-6\"",
            "model id must be the resolved Sonnet id");
    }

    [Fact]
    public async Task AnalyzeAsync_RequestBodyContainsCorrectMaxTokens()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        handler.LastRequestBody.Should().Contain("\"max_tokens\":8000",
            "max_tokens must match prompt.MaxTokens");
    }

    [Fact]
    public async Task AnalyzeAsync_RequestBodyContainsSystemAndUserContent()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);
        var prompt = SonnetReviewPrompt();

        await sut.AnalyzeAsync(prompt, CancellationToken.None);

        handler.LastRequestBody.Should().Contain(prompt.System,
            "system prompt must be present in request body");
        handler.LastRequestBody.Should().Contain("What is polymorphism?",
            "user message content must be present in request body");
    }

    [Fact]
    public async Task AnalyzeAsync_RequestIsNonStreaming()
    {
        var sut = MakeSutWithTracker(ApiResponse("## Questions Asked\n- Q?"), out var handler);

        await sut.AnalyzeAsync(SonnetReviewPrompt(), CancellationToken.None);

        // Either stream field is absent (non-streaming default) or explicitly false
        if (handler.LastRequestBody!.Contains("\"stream\""))
        {
            handler.LastRequestBody.Should().Contain("\"stream\":false",
                "session review must be non-streaming");
        }
    }

    [Fact]
    public async Task AnalyzeAsync_CancellationRequested_PropagatesCancellation()
    {
        var neverHandler = new ReviewNeverResolvingHandler();
        var http = new HttpClient(neverHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));
        var sut = new SessionReviewAnalyzer(http, secrets, Options.Create(new ClaudeOptions()));

        using var cts = new CancellationTokenSource();
        var task = sut.AnalyzeAsync(SonnetReviewPrompt(), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
}

// ─── Test infrastructure (internal, not file-local, so they can be used in signatures) ──

internal sealed class ReviewMockHandler(
    string body,
    HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    /// <summary>
    /// Snapshot of the x-api-key header value captured during SendAsync, before
    /// SecureStringHelpers.ZeroString runs in the caller's finally block.
    /// </summary>
    public string? CapturedApiKey { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = await request.Content!.ReadAsStringAsync(ct);
        // Snapshot the API key as an independent copy before the caller's finally
        // block calls ZeroString, which zeroes the char buffer of the original string in-place.
        if (request.Headers.TryGetValues("x-api-key", out var vals) &&
            vals.FirstOrDefault() is { } rawKey)
        {
            // new string(span) always allocates; the original string object will be zeroed later.
            CapturedApiKey = new string(rawKey.AsSpan());
        }
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ReviewCallCountHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(respond());
}

internal sealed class ReviewNeverResolvingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
