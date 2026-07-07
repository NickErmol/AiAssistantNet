using System.Net;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Infrastructure.AI;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public sealed class ClaudeAnswerModelTests
{
    [Theory]
    [InlineData(AnswerModel.Haiku,  "claude-haiku-4-5-20251001")]
    [InlineData(AnswerModel.Sonnet, "claude-sonnet-4-6")]
    [InlineData(AnswerModel.Opus,   "claude-opus-4-8")]
    public void Resolve_MapsEachModel(AnswerModel model, string expectedId)
        => ClaudeModels.Resolve(model).Should().Be(expectedId);

    [Fact]
    public async Task StreamAnswerAsync_UsesSelectedModel_InRequestBody()
    {
        var capture = new CapturingAnswerHandler();
        var http = new HttpClient(capture) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Ok(ss));
        var sut = new ClaudeAnswerProvider(http, secrets, Options.Create(new ClaudeOptions()));

        var prompt = new AnswerPrompt("sys", "user", "English", 300, AnswerModel.Sonnet);
        await foreach (var _ in sut.StreamAnswerAsync(prompt, CancellationToken.None)) { }

        capture.LastRequestBody.Should().Contain("\"model\":\"claude-sonnet-4-6\"");
    }

    [Fact]
    public async Task StreamAnswerAsync_FallsBackToOptionsModel_WhenPromptModelNull()
    {
        var capture = new CapturingAnswerHandler();
        var http = new HttpClient(capture) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey(SecretKind.Anthropic).Returns(Result.Ok(ss));
        var sut = new ClaudeAnswerProvider(
            http, secrets, Options.Create(new ClaudeOptions { Model = "configured-default" }));

        var prompt = new AnswerPrompt("sys", "user", "English", 300);
        await foreach (var _ in sut.StreamAnswerAsync(prompt, CancellationToken.None)) { }

        capture.LastRequestBody.Should().Contain("\"model\":\"configured-default\"");
    }
}

file sealed class CapturingAnswerHandler : HttpMessageHandler
{
    public string LastRequestBody { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
    {
        LastRequestBody = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
        // Minimal SSE stream: one text delta then DONE.
        const string sse = "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"hi\"}}\n\ndata: [DONE]\n\n";
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, System.Text.Encoding.UTF8, "text/event-stream")
        };
    }
}
