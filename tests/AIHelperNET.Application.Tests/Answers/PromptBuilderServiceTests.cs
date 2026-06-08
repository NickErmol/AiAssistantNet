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

    // ── Context-aware prompting ────────────────────────────────────────────────
    [Fact]
    public void Build_WithNoContext_UserContainsOnlyQuestion()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is the pattern?");

        prompt.User.Should().Contain("Question: What is the pattern?");
        prompt.User.Should().NotContain("Conversation context");
    }

    [Fact]
    public void Build_WithTranscriptContext_InjectsTranscriptBlock()
    {
        var items = new List<TranscriptItem>
        {
            TranscriptItem.Create(Speaker.Other, "You tell me about fabric decorator builder", Now, 0.9f),
            TranscriptItem.Create(Speaker.Me,    "What is the pattern?", Now.AddSeconds(10), 0.9f),
        };

        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is the pattern?",
            recentTranscript: items);

        prompt.User.Should().Contain("Conversation context");
        prompt.User.Should().Contain("[Transcript] Interviewer: You tell me about fabric decorator builder");
        prompt.User.Should().Contain("[Transcript] Me: What is the pattern?");
        prompt.User.Should().Contain("Question: What is the pattern?");
    }

    [Fact]
    public void Build_WithQAContext_InjectsQABlock()
    {
        var qa = new List<(string Question, string Answer)>
        {
            ("What is a design pattern?", "A design pattern is a reusable solution to a common problem."),
        };

        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is the pattern?",
            recentQA: qa);

        prompt.User.Should().Contain("Conversation context");
        prompt.User.Should().Contain("[Q&A] Q: What is a design pattern?");
        prompt.User.Should().Contain("A design pattern is a reusable solution");
        prompt.User.Should().Contain("[Q&A] Q: What is a design pattern?  A: A design pattern is a reusable solution");
    }

    [Fact]
    public void Build_WithBothContextTypes_InjectsBothBlocks()
    {
        var items = new List<TranscriptItem>
        {
            TranscriptItem.Create(Speaker.Other, "You tell me about patterns", Now, 0.9f),
        };
        var qa = new List<(string Question, string Answer)>
        {
            ("What is OOP?", "OOP stands for Object-Oriented Programming."),
        };

        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is the pattern?",
            recentTranscript: items,
            recentQA: qa);

        prompt.User.Should().Contain("[Transcript] Interviewer: You tell me about patterns");
        prompt.User.Should().Contain("[Q&A] Q: What is OOP?");
        prompt.User.Should().Contain("Question: What is the pattern?");
    }

    [Fact]
    public void Build_QAAnswerLongerThan400Chars_IsTruncated()
    {
        var longAnswer = new string('A', 450);
        var qa = new List<(string Question, string Answer)>
        {
            ("Short question?", longAnswer),
        };

        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is the pattern?",
            recentQA: qa);

        // The answer in the prompt should be capped at 400 chars + ellipsis
        prompt.User.Should().NotContain(longAnswer);       // full 450-char string absent
        prompt.User.Should().Contain(new string('A', 400)); // first 400 chars present
    }

    [Fact]
    public void Build_WithScreenContextAndTranscript_BothAppearInUser()
    {
        var items = new List<TranscriptItem>
        {
            TranscriptItem.Create(Speaker.Other, "Explain this code", Now, 0.9f),
        };

        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default,
            "What does this do?",
            screenContext: "void Main() { }",
            recentTranscript: items);

        prompt.User.Should().Contain("[Transcript] Interviewer: Explain this code");
        prompt.User.Should().Contain("void Main()");
        prompt.User.Should().Contain("Question: What does this do?");
    }

    [Fact]
    public void Build_ClipsPriorAnswerContextAt400Chars()
    {
        var longAnswer = new string('A', 500);
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty,
            AnswerSettings.Default,
            "What is DI?",
            screenContext: null,
            recentTranscript: null,
            recentQA: new List<(string, string)> { ("Earlier question?", longAnswer) });

        prompt.User.Should().Contain(new string('A', 400) + "…");
        prompt.User.Should().NotContain(new string('A', 401));
    }
}
