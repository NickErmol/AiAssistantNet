using System.Net;
using System.Net.Http;
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>Unit tests for <see cref="AnswerErrorMessage"/>.</summary>
public class AnswerErrorMessageTests
{
    // ── 401 / 403  ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public void ForUser_HttpRequestException_AuthStatusCode_ReturnsAuthMessage(HttpStatusCode status)
    {
        var ex = new HttpRequestException("401 Unauthorized: {\"type\":\"error\"}", null, status);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("Authentication failed — check your API key in Settings.");
    }

    // ── 429  ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ForUser_HttpRequestException_429_ReturnsRateLimitMessage()
    {
        var ex = new HttpRequestException("429 Too Many Requests", null, HttpStatusCode.TooManyRequests);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("Rate limit reached — please wait a moment and try again.");
    }

    // ── 529 (Anthropic overloaded — no named enum value)  ────────────────────
    [Fact]
    public void ForUser_HttpRequestException_529_ReturnsUnavailableMessage()
    {
        var ex = new HttpRequestException("529 Overloaded", null, (HttpStatusCode)529);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("The AI service is temporarily unavailable. Please try again.");
    }

    // ── 503  ─────────────────────────────────────────────────────────────────
    [Fact]
    public void ForUser_HttpRequestException_503_ReturnsUnavailableMessage()
    {
        var ex = new HttpRequestException("503 Service Unavailable", null, HttpStatusCode.ServiceUnavailable);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("The AI service is temporarily unavailable. Please try again.");
    }

    // ── Other 5xx  ───────────────────────────────────────────────────────────
    [Theory]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public void ForUser_HttpRequestException_Other5xx_ReturnsUnavailableMessage(int code)
    {
        var ex = new HttpRequestException($"{code} Server Error", null, (HttpStatusCode)code);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("The AI service is temporarily unavailable. Please try again.");
    }

    // ── InvalidOperationException with "API key" message  ────────────────────
    [Fact]
    public void ForUser_InvalidOperationException_NoApiKey_ReturnsNoKeyMessage()
    {
        var ex = new InvalidOperationException("No Claude API key configured.");

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("No API key configured — add one in Settings.");
    }

    // ── HttpRequestException with null StatusCode (network / DNS failure)  ───
    [Fact]
    public void ForUser_HttpRequestException_NullStatusCode_ReturnsConnectivityMessage()
    {
        // HttpRequestException with no status code overload
        var ex = new HttpRequestException("The request failed due to an underlying issue.");

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("Couldn't reach the AI service — check your connection.");
    }

    // ── Fallback (any other exception type)  ─────────────────────────────────
    [Fact]
    public void ForUser_UnknownException_ReturnsFallbackMessage()
    {
        var ex = new InvalidDataException("Something unexpected happened.");

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().Be("Something went wrong generating the answer. Please try again.");
    }

    // ── No raw JSON / body leakage  ──────────────────────────────────────────
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public void ForUser_NeverLeaksRawBody_ForHttpErrors(HttpStatusCode status)
    {
        var rawJson = "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}";
        var ex = new HttpRequestException($"{(int)status} Error: {rawJson}", null, status);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().NotContain("{");
        msg.Should().NotContain("}");
        msg.Should().NotContain("x-api-key");
    }

    [Fact]
    public void ForUser_NeverLeaksRawBody_For529()
    {
        var rawJson = "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\"}}";
        var ex = new HttpRequestException($"529 Overloaded: {rawJson}", null, (HttpStatusCode)529);

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().NotContain("{");
        msg.Should().NotContain("}");
        msg.Should().NotContain("x-api-key");
    }

    [Fact]
    public void ForUser_NeverLeaksRawBody_ForNullStatusNetworkError()
    {
        var ex = new HttpRequestException("No such host: {weird} data} here");

        var msg = AnswerErrorMessage.ForUser(ex);

        msg.Should().NotContain("{");
        msg.Should().NotContain("}");
    }
}
