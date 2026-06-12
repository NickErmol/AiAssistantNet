using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class AppSettingsDtoTests
{
    private static AppSettingsDto Base() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public void Default_MaxAnswerTokens_Is800()
        => Base().MaxAnswerTokens.Should().Be(800);

    [Fact]
    public void Normalized_KeepsInRangeValue()
        => (Base() with { MaxAnswerTokens = 1200 }).Normalized().MaxAnswerTokens.Should().Be(1200);

    [Fact]
    public void Normalized_ClampsBelowMin()
        => (Base() with { MaxAnswerTokens = 50 }).Normalized().MaxAnswerTokens.Should().Be(200);

    [Fact]
    public void Normalized_ClampsAboveMax()
        => (Base() with { MaxAnswerTokens = 9000 }).Normalized().MaxAnswerTokens.Should().Be(4000);

    [Fact]
    public void Normalized_CoercesMissingOrZeroToDefault()
        => (Base() with { MaxAnswerTokens = 0 }).Normalized().MaxAnswerTokens.Should().Be(800);

    [Fact]
    public void Normalized_KeepsInRangeLatestQuestionWindow()
        => (Base() with { LatestQuestionWindowSeconds = 90 }).Normalized()
            .LatestQuestionWindowSeconds.Should().Be(90);

    [Fact]
    public void Normalized_ClampsLatestQuestionWindowBelowMin()
        => (Base() with { LatestQuestionWindowSeconds = 5 }).Normalized()
            .LatestQuestionWindowSeconds.Should().Be(AppSettingsDto.MinLatestQuestionWindowSeconds);

    [Fact]
    public void Normalized_ClampsLatestQuestionWindowAboveMax()
        => (Base() with { LatestQuestionWindowSeconds = 9999 }).Normalized()
            .LatestQuestionWindowSeconds.Should().Be(AppSettingsDto.MaxLatestQuestionWindowSeconds);

    [Fact]
    public void Normalized_CoercesMissingOrZeroLatestQuestionWindowToDefault()
        => (Base() with { LatestQuestionWindowSeconds = 0 }).Normalized()
            .LatestQuestionWindowSeconds.Should().Be(AppSettingsDto.DefaultLatestQuestionWindowSeconds);

    [Fact]
    public void LatestQuestionWindowSeconds_DefaultsTo120()
        => Base().LatestQuestionWindowSeconds.Should().Be(120);
}
