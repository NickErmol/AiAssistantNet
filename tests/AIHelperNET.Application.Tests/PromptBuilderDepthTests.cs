using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class PromptBuilderDepthTests
{
    private const string DifficultyMarker = "Match depth to the question";

    [Fact]
    public void Build_UsesExplicitMaxTokens_WhenProvided()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is a primary key?", maxTokens: 1234);
        prompt.MaxTokens.Should().Be(1234);
    }

    [Fact]
    public void Build_FallsBackToLengthMapping_WhenMaxTokensNull()
    {
        // AnswerSettings.Default.Length == ShortLength → MapLengthToTokens == 300
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is a primary key?");
        prompt.MaxTokens.Should().Be(300);
    }

    [Fact]
    public void Build_AudioPrompt_ContainsDifficultyInstruction()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "Explain the CAP theorem");
        prompt.System.Should().Contain(DifficultyMarker);
    }

    [Fact]
    public void ScreenAndFollowUpPrompts_OmitDifficultyInstruction()
    {
        var screen = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default, "some code",
            System.Array.Empty<string>(), ScreenAnalysisMode.General);
        var follow = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, AnswerSettings.Default, "q", "prev answer", "follow up");
        screen.System.Should().NotContain(DifficultyMarker);
        follow.System.Should().NotContain(DifficultyMarker);
    }
}
