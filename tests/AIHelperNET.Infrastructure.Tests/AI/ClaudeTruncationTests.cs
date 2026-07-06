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

/// <summary>
/// A max_tokens cutoff must be visible: in the 2026-07-06 session an answer card ended mid-sentence
/// with no indication it was incomplete. The SSE parser surfaces stop_reason, and the provider
/// appends a visible truncation marker when generation hit the cap.
/// </summary>
public sealed class ClaudeTruncationTests
{
    // --- ClaudeSse.ParseStopReason -------------------------------------------------------------

    [Fact]
    public void ParseStopReason_MessageDeltaMaxTokens_ReturnsMaxTokens()
        => ClaudeSse.ParseStopReason(
                """{"type":"message_delta","delta":{"stop_reason":"max_tokens","stop_sequence":null},"usage":{"output_tokens":300}}""")
            .Should().Be("max_tokens");

    [Fact]
    public void ParseStopReason_MessageDeltaEndTurn_ReturnsEndTurn()
        => ClaudeSse.ParseStopReason(
                """{"type":"message_delta","delta":{"stop_reason":"end_turn","stop_sequence":null},"usage":{"output_tokens":42}}""")
            .Should().Be("end_turn");

    [Theory]
    [InlineData("""{"type":"content_block_delta","delta":{"type":"text_delta","text":"hi"}}""")]
    [InlineData("""{"type":"message_start"}""")]
    [InlineData("not json")]
    public void ParseStopReason_OtherEvents_ReturnNull(string json)
        => ClaudeSse.ParseStopReason(json).Should().BeNull();

    // --- provider appends a marker on max_tokens -------------------------------------------------

    private static ClaudeAnswerProvider MakeSut(string sseBody)
    {
        var handler = new SseMockHandler(sseBody);
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com") };
        var secrets = Substitute.For<ISecretStore>();
        var ss = new SecureString();
        foreach (var c in "fake-key") ss.AppendChar(c);
        ss.MakeReadOnly();
        secrets.GetApiKey().Returns(Result.Ok(ss));
        return new ClaudeAnswerProvider(http, secrets, Options.Create(new ClaudeOptions()));
    }

    private static AnswerPrompt Prompt() => new("system", "user", "English", 300);

    private const string TruncatedSse =
        "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\"}}\n" +
        "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Token refresh catches expired or over-\"}}\n" +
        "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":300}}\n" +
        "data: {\"type\":\"message_stop\"}\n";

    private const string CompleteSse =
        "data: {\"type\":\"message_start\",\"message\":{\"id\":\"msg_01\"}}\n" +
        "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Done.\"}}\n" +
        "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\",\"stop_sequence\":null},\"usage\":{\"output_tokens\":5}}\n" +
        "data: {\"type\":\"message_stop\"}\n";

    [Fact]
    public async Task MaxTokensCutoff_AppendsVisibleTruncationMarker()
    {
        var sut = MakeSut(TruncatedSse);
        var chunks = new List<string>();
        await foreach (var c in sut.StreamAnswerAsync(Prompt(), CancellationToken.None))
            chunks.Add(c);

        string.Concat(chunks).Should().Contain("cut off",
            "the user must see that the answer is incomplete instead of trusting a mid-sentence stop");
    }

    [Fact]
    public async Task NormalCompletion_AppendsNoMarker()
    {
        var sut = MakeSut(CompleteSse);
        var chunks = new List<string>();
        await foreach (var c in sut.StreamAnswerAsync(Prompt(), CancellationToken.None))
            chunks.Add(c);

        string.Concat(chunks).Should().Be("Done.");
    }

    private sealed class SseMockHandler(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body)
            });
    }
}
