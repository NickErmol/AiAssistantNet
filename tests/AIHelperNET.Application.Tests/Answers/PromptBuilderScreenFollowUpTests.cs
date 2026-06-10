using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class PromptBuilderScreenFollowUpTests
{
    private static readonly string[] TwoAdditions = ["make it thread-safe", "handle nulls"];
    private static readonly string[] OneTranscriptLine = ["Interviewer: and handle nulls"];
    private static readonly string[] OneAddition = ["do X"];

    [Fact]
    public void BuildScreenFollowUp_FencesContext_Accumulates_AndKeeps2000Floor()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            screenContext: "Implement an LRU cache",
            mode: ScreenAnalysisMode.SolveCodingTask,
            additions: TwoAdditions,
            recentTranscript: OneTranscriptLine,
            priorAnswer: "class LruCache { }");

        prompt.System.Should().Contain("If they added conditions");          // decision instruction
        prompt.User.Should().Contain("On-screen task (OCR):");
        prompt.User.Should().Contain("Implement an LRU cache");
        prompt.User.Should().Contain("1. make it thread-safe");
        prompt.User.Should().Contain("2. handle nulls");                     // accumulation, ordered
        prompt.User.Should().Contain("Recent conversation:");
        prompt.User.Should().Contain("Your previous answer:");
        prompt.MaxTokens.Should().BeGreaterThanOrEqualTo(2000);
    }

    [Fact]
    public void BuildScreenFollowUp_OmitsPriorAnswerSection_WhenNull()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default, "task",
            ScreenAnalysisMode.SolveCodingTask,
            additions: OneAddition, recentTranscript: Array.Empty<string>(), priorAnswer: null);

        prompt.User.Should().NotContain("Your previous answer:");
    }
}
