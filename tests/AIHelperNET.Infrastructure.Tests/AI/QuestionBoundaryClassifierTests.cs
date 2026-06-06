using System.Net;
using System.Net.Http;
using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Infrastructure.AI;
using FluentResults;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.AI;

public sealed class QuestionBoundaryClassifierTests
{
    private static QuestionBoundaryClassifier MakeSut(
        string responseBody,
        HttpStatusCode status = HttpStatusCode.OK,
        bool hasApiKey = true)
    {
        var handler = new BoundaryMockHttpMessageHandler(responseBody, status);
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

        var options = Options.Create(new ClaudeOptions());
        return new QuestionBoundaryClassifier(http, secrets, options);
    }

    private static TranscriptItem MakeItem(string text)
        => TranscriptItem.Create(Speaker.Other, text, DateTimeOffset.UnixEpoch, 0.9f);

    /// <summary>Wraps inner JSON in the Anthropic Messages API response envelope.</summary>
    private static string MakeApiResponse(string innerJson) =>
        // Escape the innerJson for embedding inside a JSON string value
        $"{{\"id\":\"msg_01\",\"type\":\"message\",\"role\":\"assistant\",\"content\":[{{\"type\":\"text\",\"text\":\"{innerJson.Replace("\"", "\\\"")}\"}}],\"model\":\"claude-haiku\",\"stop_reason\":\"end_turn\",\"usage\":{{\"input_tokens\":10,\"output_tokens\":50}}}}";

    // ── 1. Valid JSON → QuestionComplete label ──────────────────────────────

    [Fact]
    public async Task ClassifyAsync_QuestionComplete_ParsedCorrectly()
    {
        // Escape braces for string interpolation inside the raw string
        var innerJson = """{"classification":"QuestionComplete","confidence":0.95,"normalized_text":"What is DDD?","reason":"explicit question"}""";
        var sut = MakeSut(MakeApiResponse(innerJson));

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("What is DDD?"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.QuestionComplete, result.Classification);
        Assert.Equal(0.95, result.Confidence, precision: 2);
        Assert.True(result.ShouldGenerateAnswer);
        Assert.True(result.ShouldCreateNewTurn);
        Assert.False(result.ShouldRefineExistingAnswer);
        Assert.Equal("What is DDD?", result.NormalizedQuestionText);
    }

    // ── 2. Spot-check label mappings ────────────────────────────────────────

    [Theory]
    [InlineData("QuestionStarted",                false, false, true)]
    [InlineData("TaskComplete",                   true,  false, true)]
    [InlineData("AdditionalRequirement",          false, true,  false)]
    [InlineData("ClarificationOfCurrentQuestion", false, true,  false)]
    [InlineData("NewQuestion",                    true,  false, true)]
    [InlineData("Unrelated",                      false, false, false)]
    [InlineData("QuestionContinued",              false, false, false)]
    [InlineData("NoQuestion",                     false, false, false)]
    public async Task ClassifyAsync_LabelMappings_FlagsAreCorrect(
        string label, bool generateAnswer, bool refineAnswer, bool createTurn)
    {
        var innerJson = $$$"""{"classification":"{{{label}}}","confidence":0.80,"normalized_text":"text","reason":"test"}""";
        var sut = MakeSut(MakeApiResponse(innerJson));

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("some text"), Speaker.Other, CancellationToken.None);

        Assert.Equal(generateAnswer, result.ShouldGenerateAnswer);
        Assert.Equal(refineAnswer, result.ShouldRefineExistingAnswer);
        Assert.Equal(createTurn, result.ShouldCreateNewTurn);
    }

    // ── 3. Unknown label → NoQuestion fallback ──────────────────────────────

    [Fact]
    public async Task ClassifyAsync_UnknownLabel_FallsBackToNoQuestion()
    {
        var innerJson = """{"classification":"SomeUnknownLabel","confidence":0.70,"normalized_text":"text","reason":"??"}""";
        var sut = MakeSut(MakeApiResponse(innerJson));

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("text"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.NoQuestion, result.Classification);
    }

    // ── 4. HTTP error response → Ambiguous ──────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_ApiError_ReturnsAmbiguous()
    {
        var sut = MakeSut("""{"error":{"type":"auth_error"}}""", HttpStatusCode.Unauthorized);

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("test"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.NoQuestion, result.Classification);
        Assert.Equal(0.30, result.Confidence, precision: 2);
    }

    // ── 5. Malformed JSON in content text → Ambiguous ───────────────────────

    [Fact]
    public async Task ClassifyAsync_MalformedInnerJson_ReturnsAmbiguous()
    {
        // The outer envelope is valid, but the inner "text" is not valid JSON
        var outerJson = """{"id":"msg","type":"message","role":"assistant","content":[{"type":"text","text":"not json at all"}],"model":"claude-haiku","stop_reason":"end_turn","usage":{"input_tokens":1,"output_tokens":1}}""";
        var sut = MakeSut(outerJson);

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("test"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.NoQuestion, result.Classification);
        Assert.Equal(0.30, result.Confidence, precision: 2);
    }

    // ── 6. No API key configured → Ambiguous ────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_NoApiKey_ReturnsAmbiguous()
    {
        var sut = MakeSut("", hasApiKey: false);

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("test"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.NoQuestion, result.Classification);
        Assert.Equal(0.30, result.Confidence, precision: 2);
        Assert.False(result.ShouldGenerateAnswer);
    }

    // ── 7. Entirely malformed outer JSON ────────────────────────────────────

    [Fact]
    public async Task ClassifyAsync_MalformedOuterJson_ReturnsAmbiguous()
    {
        var sut = MakeSut("this is not json");

        var result = await sut.ClassifyAsync(
            null, [], MakeItem("test"), Speaker.Other, CancellationToken.None);

        Assert.Equal(BoundaryLabel.NoQuestion, result.Classification);
    }
}

file sealed class BoundaryMockHttpMessageHandler(string body, HttpStatusCode status = HttpStatusCode.OK)
    : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken ct)
        => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        });
}
