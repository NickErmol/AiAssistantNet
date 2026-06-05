using System.Net;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Infrastructure.AI;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public class HaikuQuestionClassifierTests
{
    private static HaikuQuestionClassifier MakeSut(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new MockHttpMessageHandler(responseBody, status);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };

        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));

        var options = Options.Create(new ClaudeOptions());
        return new HaikuQuestionClassifier(http, secrets, options);
    }

    private static string MakeResponse(string text) =>
        $$$"""{"id":"msg_01","type":"message","role":"assistant","content":[{"type":"text","text":"{{{text}}}"}],"model":"claude-haiku","stop_reason":"end_turn","usage":{"input_tokens":10,"output_tokens":1}}""";

    [Theory]
    [InlineData("NewQuestion",   ClassificationResult.NewQuestion)]
    [InlineData("Continuation",  ClassificationResult.Continuation)]
    [InlineData("NotAQuestion",  ClassificationResult.NotAQuestion)]
    [InlineData("garbage text",  ClassificationResult.NotAQuestion)]
    [InlineData("",              ClassificationResult.NotAQuestion)]
    public async Task ClassifyAsync_ParsesApiResponse(string apiText, ClassificationResult expected)
    {
        var sut = MakeSut(MakeResponse(apiText));
        var result = await sut.ClassifyAsync("How do you handle DI?", [], CancellationToken.None);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task ClassifyAsync_ApiError_ReturnsNotAQuestion()
    {
        var sut = MakeSut("""{"error":{"type":"auth_error"}}""", HttpStatusCode.Unauthorized);
        var result = await sut.ClassifyAsync("test", [], CancellationToken.None);
        Assert.Equal(ClassificationResult.NotAQuestion, result);
    }

    [Fact]
    public async Task ClassifyAsync_MalformedJson_ReturnsNotAQuestion()
    {
        var sut = MakeSut("not json at all");
        var result = await sut.ClassifyAsync("test", [], CancellationToken.None);
        Assert.Equal(ClassificationResult.NotAQuestion, result);
    }

    [Fact]
    public async Task ClassifyAsync_IncludesRecentQuestionsInRequest()
    {
        var handler = new CapturingHttpMessageHandler(MakeResponse("NewQuestion"));
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "k") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));
        var sut = new HaikuQuestionClassifier(http, secrets, Options.Create(new ClaudeOptions()));

        await sut.ClassifyAsync("new question", ["Q1?", "Q2?"], CancellationToken.None);

        Assert.Contains("Q1?", handler.LastRequestBody);
        Assert.Contains("Q2?", handler.LastRequestBody);
    }
}

file sealed class MockHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}

file sealed class CapturingHttpMessageHandler(string body) : HttpMessageHandler
{
    public string LastRequestBody { get; private set; } = string.Empty;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestBody = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(""));
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
    }
}
