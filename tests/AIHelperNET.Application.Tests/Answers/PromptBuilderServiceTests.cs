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
    public void Build_NonDefaultComplexity_IncludedInSystem()
    {
        var settings = AnswerSettings.Default with { Complexity = AnswerComplexity.Senior };
        var question = DetectedQuestion.Create("Explain event sourcing", QuestionSource.Audio, Now);

        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, question);

        prompt.System.Should().Contain("Senior");
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
    [InlineData(AnswerLength.VeryShort, 150)]
    [InlineData(AnswerLength.ShortLength, 300)]
    [InlineData(AnswerLength.Medium, 550)]
    [InlineData(AnswerLength.Detailed, 1000)]
    [InlineData(AnswerLength.DeepDive, 2000)]
    public void Build_MapsLengthToTokens(AnswerLength length, int expected)
    {
        var settings = AnswerSettings.Default with { Length = length };
        var question = DetectedQuestion.Create("Test question?", QuestionSource.Audio, Now);
        var prompt = PromptBuilderService.Build(CodeProfile.Empty, settings, question);
        prompt.MaxTokens.Should().Be(expected);
    }

    [Fact]
    public void BuildFollowUp_InjectsOriginalQAndA_InUserPrompt()
    {
        var prompt = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty,
            AnswerSettings.Default,
            "What is CQRS?",
            "CQRS separates reads and writes.",
            "Can you give an example?");

        prompt.User.Should().Contain("What is CQRS?");
        prompt.User.Should().Contain("CQRS separates reads and writes.");
        prompt.User.Should().Contain("Can you give an example?");
    }
}
