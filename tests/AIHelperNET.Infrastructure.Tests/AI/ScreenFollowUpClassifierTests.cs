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

public sealed class ScreenFollowUpClassifierTests
{
    private static ScreenFollowUpClassifier MakeSut(
        string responseBody, HttpStatusCode status = HttpStatusCode.OK, bool hasApiKey = true)
    {
        var handler = new ScreenFollowUpMockHandler(responseBody, status);
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

        return new ScreenFollowUpClassifier(http, secrets, Options.Create(new ClaudeOptions()));
    }

    private static string ApiResponse(string innerJson) =>
        $"{{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{innerJson.Replace("\"", "\\\"")}\"}}],\"model\":\"claude-haiku\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":10,\"output_tokens\":5}}}}";

    private static Task<ScreenFollowUpOutcome> Classify(ScreenFollowUpClassifier sut) =>
        sut.ClassifyAsync("Implement an LRU cache in C#", [], "now make it thread-safe", CancellationToken.None);

    [Theory]
    [InlineData("FOLLOWUP", ScreenFollowUpOutcome.FollowUp)]
    [InlineData("MOVED_ON", ScreenFollowUpOutcome.MovedOn)]
    [InlineData("NOISE",    ScreenFollowUpOutcome.Noise)]
    public async Task ClassifyAsync_MapsDecision(string decision, ScreenFollowUpOutcome expected)
    {
        var sut = MakeSut(ApiResponse($$"""{"decision":"{{decision}}"}"""));
        Assert.Equal(expected, await Classify(sut));
    }

    [Fact]
    public async Task ClassifyAsync_StripsMarkdownCodeFence()
    {
        // Haiku often wraps JSON in a ```json fence despite instructions — parse it anyway.
        var sut = MakeSut(ApiResponse("```json\\n{\"decision\":\"FOLLOWUP\"}\\n```"));
        Assert.Equal(ScreenFollowUpOutcome.FollowUp, await Classify(sut));
    }

    [Fact]
    public async Task ClassifyAsync_UnknownDecision_BiasesToFollowUp()
    {
        // The model ran and returned valid JSON but an unexpected value — keep the task in context.
        var sut = MakeSut(ApiResponse("""{"decision":"maybe?"}"""));
        Assert.Equal(ScreenFollowUpOutcome.FollowUp, await Classify(sut));
    }

    [Fact]
    public async Task ClassifyAsync_ApiError_ReturnsNoise()
    {
        var sut = MakeSut("""{"error":{"type":"auth_error"}}""", HttpStatusCode.Unauthorized);
        Assert.Equal(ScreenFollowUpOutcome.Noise, await Classify(sut));
    }

    [Fact]
    public async Task ClassifyAsync_NoApiKey_ReturnsNoise()
    {
        var sut = MakeSut("", hasApiKey: false);
        Assert.Equal(ScreenFollowUpOutcome.Noise, await Classify(sut));
    }

    [Fact]
    public async Task ClassifyAsync_MalformedJson_ReturnsNoise()
    {
        var sut = MakeSut("this is not json");
        Assert.Equal(ScreenFollowUpOutcome.Noise, await Classify(sut));
    }
}

file sealed class ScreenFollowUpMockHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}
