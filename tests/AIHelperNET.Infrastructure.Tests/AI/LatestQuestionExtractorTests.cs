using System.Net;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Infrastructure.AI;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public sealed class LatestQuestionExtractorTests
{
    private static LatestQuestionExtractor MakeSut(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK, bool hasApiKey = true)
    {
        var handler = new LatestQuestionMockHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };

        var secrets = Substitute.For<ISecretStore>();
        if (hasApiKey)
        {
            var ss = new SecureString();
            foreach (var c in "fake-key") ss.AppendChar(c);
            ss.MakeReadOnly();
            secrets.GetApiKey().Returns(Result.Ok(ss));
        }
        else
        {
            secrets.GetApiKey().Returns(Result.Fail<SecureString>("no key"));
        }

        return new LatestQuestionExtractor(http, secrets, Options.Create(new ClaudeOptions()));
    }

    private static string ApiResponse(string innerJson) =>
        $"{{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{innerJson.Replace("\"", "\\\"")}\"}}],\"model\":\"claude-haiku\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":10,\"output_tokens\":5}}}}";

    private static IReadOnlyList<TranscriptLine> OneLineWindow() =>
        [new TranscriptLine("Interviewer", "How would you design a rate limiter?")];

    private static Task<LatestQuestionResult> Extract(LatestQuestionExtractor sut) =>
        sut.ExtractAsync(OneLineWindow(), screenContext: null, CancellationToken.None);

    [Fact]
    public async Task ExtractAsync_HappyPath_ReturnsParsedResult()
    {
        var sut = MakeSut(ApiResponse(
            """{"found":true,"question":"How would you design a rate limiter?","context":"Discussing distributed APIs."}"""));

        var result = await Extract(sut);

        Assert.True(result.Found);
        Assert.Equal("How would you design a rate limiter?", result.QuestionText);
        Assert.Equal("Discussing distributed APIs.", result.ContextSummary);
    }

    [Fact]
    public async Task ExtractAsync_StripsMarkdownCodeFence()
    {
        // Haiku often wraps JSON in a ```json fence despite instructions — parse it anyway.
        var sut = MakeSut(ApiResponse("```json\\n{\"found\":true,\"question\":\"Q?\",\"context\":\"\"}\\n```"));

        var result = await Extract(sut);

        Assert.True(result.Found);
        Assert.Equal("Q?", result.QuestionText);
    }

    [Fact]
    public async Task ExtractAsync_FoundFalse_ReturnsNotFound()
    {
        var sut = MakeSut(ApiResponse("""{"found":false,"question":"","context":""}"""));

        var result = await Extract(sut);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ExtractAsync_WhitespaceOnlyQuestion_ReturnsNotFound()
    {
        var sut = MakeSut(ApiResponse("""{"found":true,"question":"   ","context":""}"""));

        var result = await Extract(sut);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ExtractAsync_MalformedModelOutput_ReturnsNotFound()
    {
        var sut = MakeSut(ApiResponse("not json at all"));

        var result = await Extract(sut);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ExtractAsync_NoApiKey_ReturnsNotFound()
    {
        var sut = MakeSut("", hasApiKey: false);

        var result = await Extract(sut);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task ExtractAsync_ApiError_ReturnsNotFound()
    {
        var sut = MakeSut("""{"error":{"type":"server_error"}}""", HttpStatusCode.InternalServerError);

        var result = await Extract(sut);

        Assert.False(result.Found);
    }
}

file sealed class LatestQuestionMockHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}
