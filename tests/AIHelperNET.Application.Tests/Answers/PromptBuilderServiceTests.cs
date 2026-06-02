using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class PromptBuilderServiceTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Build_IncludesCodeProfileInSystem()
    {
        var profile = CodeProfile.Empty with { ProgrammingLanguage = "C#", Database = "PostgreSQL" };
        var question = DetectedQuestion.Create("Explain async/await", QuestionSource.Audio, Now);

        var prompt = PromptBuilderService.Build(profile, AnswerSettings.Default, question);

        prompt.System.Should().Contain("C#");
        prompt.System.Should().Contain("PostgreSQL");
    }

    [Fact]
    public void Build_IncludesAnswerSettingsInSystem()
    {
        var settings = AnswerSettings.Default with { Style = AnswerStyle.CodeFirst };
        var question = DetectedQuestion.Create("Write a LINQ query", QuestionSource.Audio, Now);

        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, question);

        prompt.System.Should().Contain("CodeFirst");
    }

    [Fact]
    public void Build_IncludesQuestionInUser()
    {
        var question = DetectedQuestion.Create("What is CQRS?", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        prompt.User.Should().Contain("What is CQRS?");
    }

    [Fact]
    public void Build_WithScreenContext_IncludesOcrInUser()
    {
        var question = DetectedQuestion.Create("Debug this", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, question, "NullReferenceException at line 42");
        prompt.User.Should().Contain("NullReferenceException at line 42");
    }

    [Fact]
    public void Build_NoRagContext_SystemHasNoKnowledgeBase()
    {
        var question = DetectedQuestion.Create("Explain SOLID", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        prompt.System.Should().NotContain("Knowledge base");
        prompt.System.Should().NotContain("Retrieved context");
        prompt.System.Should().NotContain("RAG");
    }

    [Fact]
    public void Build_EmptyProfile_NoProfileLines()
    {
        var question = DetectedQuestion.Create("Tell me about yourself", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, question);
        prompt.System.Should().NotContain(": \n");
    }

    [Theory]
    [InlineData(AnswerLength.VeryShort, 200)]
    [InlineData(AnswerLength.ShortLength, 400)]
    [InlineData(AnswerLength.Medium, 800)]
    [InlineData(AnswerLength.Detailed, 1500)]
    [InlineData(AnswerLength.DeepDive, 3000)]
    public void Build_MapsLengthToTokens(AnswerLength length, int expected)
    {
        var settings = AnswerSettings.Default with { Length = length };
        var question = DetectedQuestion.Create("Test question?", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, question);
        prompt.MaxTokens.Should().Be(expected);
    }
}
