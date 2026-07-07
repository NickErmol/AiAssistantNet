using System.Net;
using System.Net.Http;
using System.Security;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.AI;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public sealed class ProfileCondenserTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static ProfileCondenser MakeSut(
        string responseBody,
        HttpStatusCode status = HttpStatusCode.OK,
        bool hasApiKey = true)
    {
        var handler = new ProfileMockHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        return BuildSut(http, hasApiKey);
    }

    private static ProfileCondenser BuildSut(HttpClient http, bool hasApiKey)
    {
        var secrets = Substitute.For<ISecretStore>();
        if (hasApiKey)
        {
            var ss = new SecureString();
            foreach (var c in "fake-condenser-key") ss.AppendChar(c);
            ss.MakeReadOnly();
            secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Ok(ss));
        }
        else
        {
            secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Fail<SecureString>("no key"));
        }

        return new ProfileCondenser(http, secrets, Options.Create(new ClaudeOptions()));
    }

    private static ProfileCondenser MakeSutWithTracker(
        string responseBody,
        out ProfileMockHandler handler,
        HttpStatusCode status = HttpStatusCode.OK)
    {
        handler = new ProfileMockHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        return BuildSut(http, hasApiKey: true);
    }

    private static string ApiResponse(string text)
    {
        var escaped = text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
        return $"{{\"id\":\"msg_profile_01\",\"type\":\"message\",\"role\":\"assistant\"," +
               $"\"content\":[{{\"type\":\"text\",\"text\":\"{escaped}\"}}]," +
               $"\"model\":\"claude-sonnet-4-6\",\"stop_reason\":\"end_turn\"," +
               $"\"usage\":{{\"input_tokens\":100,\"output_tokens\":200}}}}";
    }

    private const string SampleResume = "John Smith\nContoso — Senior Engineer (3 years)\nC#, Azure";
    private const string SampleJd = "Senior .NET Engineer, Azure, microservices required";

    // ─── Tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CondenseAsync_HappyPath_ReturnsCondensedText()
    {
        const string condensed = "**CANDIDATE PROFILE**\nSenior .NET engineer.";
        var sut = MakeSut(ApiResponse(condensed));

        var result = await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(condensed);
    }

    [Fact]
    public async Task CondenseAsync_WithoutJd_HappyPath_ReturnsCondensedText()
    {
        const string condensed = "**CANDIDATE PROFILE**\nSenior .NET engineer. No JD provided.";
        var sut = MakeSut(ApiResponse(condensed));

        var result = await sut.CondenseAsync(SampleResume, null, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(condensed);
    }

    [Fact]
    public async Task CondenseAsync_SendsRequestToMessagesEndpoint()
    {
        var sut = MakeSutWithTracker(ApiResponse("**CANDIDATE PROFILE**\nOK"), out var handler);

        await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        handler.LastRequest.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
    }

    [Fact]
    public async Task CondenseAsync_RequestBodyContainsSonnetModelId()
    {
        var sut = MakeSutWithTracker(ApiResponse("**CANDIDATE PROFILE**\nOK"), out var handler);

        await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        handler.LastRequestBody.Should().Contain("\"claude-sonnet-4-6\"",
            "model id must be the resolved Sonnet id");
    }

    [Fact]
    public async Task CondenseAsync_NoApiKey_ReturnsFail()
    {
        var sut = MakeSut("", hasApiKey: false);

        var result = await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle().Which.Message.Should()
            .Contain("API key");
    }

    [Fact]
    public async Task CondenseAsync_NoApiKey_DoesNotCallHttp()
    {
        var callCount = 0;
        var countingHandler = new ProfileCallCountHandler(() =>
        {
            callCount++;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });
        var http = new HttpClient(countingHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Fail<SecureString>("no key"));
        var sut = new ProfileCondenser(http, secrets, Options.Create(new ClaudeOptions()));

        await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        callCount.Should().Be(0, "HTTP must not be reached when no key is configured");
    }

    [Fact]
    public async Task CondenseAsync_Http500_ReturnsFail()
    {
        var sut = MakeSut(
            """{"error":{"type":"server_error","message":"internal"}}""",
            HttpStatusCode.InternalServerError);

        var result = await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task CondenseAsync_EmptyContentArray_ReturnsFail()
    {
        const string emptyContent =
            """{"id":"msg_01","type":"message","role":"assistant","content":[],"model":"claude-sonnet-4-6","stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":0}}""";
        var sut = MakeSut(emptyContent);

        var result = await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task CondenseAsync_CancellationRequested_PropagatesCancellation()
    {
        var neverHandler = new ProfileNeverResolvingHandler();
        var http = new HttpClient(neverHandler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Ok(ss));
        var sut = new ProfileCondenser(http, secrets, Options.Create(new ClaudeOptions()));

        using var cts = new CancellationTokenSource();
        var task = sut.CondenseAsync(SampleResume, SampleJd, cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task CondenseAsync_RequestBodyIsNonStreaming()
    {
        var sut = MakeSutWithTracker(ApiResponse("**CANDIDATE PROFILE**\nOK"), out var handler);

        await sut.CondenseAsync(SampleResume, SampleJd, CancellationToken.None);

        if (handler.LastRequestBody!.Contains("\"stream\""))
        {
            handler.LastRequestBody.Should().Contain("\"stream\":false",
                "condensation must be non-streaming");
        }
    }
}

// ─── Test infrastructure ──────────────────────────────────────────────────────

internal sealed class ProfileMockHandler(
    string body,
    HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }
    public string? LastRequestBody { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequest = request;
        LastRequestBody = await request.Content!.ReadAsStringAsync(ct);
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}

internal sealed class ProfileCallCountHandler(Func<HttpResponseMessage> respond) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(respond());
}

internal sealed class ProfileNeverResolvingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        await Task.Delay(Timeout.Infinite, ct);
        return new HttpResponseMessage(HttpStatusCode.OK);
    }
}
