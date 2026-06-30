using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class AnswerLanguageResolverTests
{
    [Theory]
    [InlineData("ru", "English", "Russian")]
    [InlineData("en", "Russian", "English")]
    [InlineData("pl", "English", "Polish")]
    [InlineData("RU", "English", "Russian")] // case-insensitive code
    public void Resolve_SpecificWhisperLanguage_OverridesFallback(
        string whisperLanguage, string fallback, string expected)
    {
        AnswerLanguageResolver.Resolve(whisperLanguage, fallback).Should().Be(expected);
    }

    [Theory]
    [InlineData("auto", "Russian")]
    [InlineData("auto", "English")]
    [InlineData("", "Russian")]
    [InlineData(null, "English")]
    public void Resolve_AutoOrEmpty_KeepsFallback(string? whisperLanguage, string fallback)
    {
        AnswerLanguageResolver.Resolve(whisperLanguage, fallback).Should().Be(fallback);
    }

    [Fact]
    public void Resolve_UnknownCode_KeepsFallback()
    {
        AnswerLanguageResolver.Resolve("zz", "English").Should().Be("English");
    }
}
