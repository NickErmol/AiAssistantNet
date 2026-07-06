using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>
/// Grounding guard: every answer prompt must forbid inventing tool/API/CLI names — in the
/// 2026-07-06 session the model produced a nonexistent CLI ("apimanagement-policy-tool") and a fake
/// APIM policy expression when fed garbage input. Length calibration: only code-producing screen
/// modes get the generous 2000-token floor; verbal screen modes (design/explain/MCQ) answer at
/// spoken length — the session's deployment-checklist answers were unspeakably long.
/// </summary>
public class PromptBuilderGroundingAndLengthTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly AnswerSettings Short = AnswerSettings.Default with { Length = AnswerLength.ShortLength };
    private static readonly string[] NoLines = [];

    // --- grounding guard ------------------------------------------------------------------------

    [Fact]
    public void Build_SystemForbidsInventingToolNames()
    {
        var question = DetectedQuestion.Create("How do you secure APIs in APIM?", QuestionSource.Audio, Now);

        var prompt = PromptBuilderService.Build(CodeProfile.Empty, Short, question);

        prompt.System.Should().ContainEquivalentOf("never invent");
    }

    [Theory]
    [InlineData(ScreenAnalysisMode.SolveCodingTask)]
    [InlineData(ScreenAnalysisMode.SystemDesign)]
    public void BuildWithScreenMode_SystemForbidsInventingToolNames(ScreenAnalysisMode mode)
    {
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, Short, "some ocr", NoLines, mode);

        prompt.System.Should().ContainEquivalentOf("never invent");
    }

    [Fact]
    public void BuildScreenFollowUp_SystemForbidsInventingToolNames()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, Short, "some ocr", ScreenAnalysisMode.SolveCodingTask, [], [], null);

        prompt.System.Should().ContainEquivalentOf("never invent");
    }

    [Fact]
    public void BuildFollowUp_SystemForbidsInventingToolNames()
    {
        var prompt = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, Short, "original q", "previous a", "follow-up q");

        prompt.System.Should().ContainEquivalentOf("never invent");
    }

    // --- screen-mode token floors -----------------------------------------------------------------

    [Theory]
    [InlineData(ScreenAnalysisMode.SolveCodingTask)]
    [InlineData(ScreenAnalysisMode.DebugError)]
    [InlineData(ScreenAnalysisMode.SystemDesign)]
    public void CodeAndDesignModes_KeepTheGenerousFloor(ScreenAnalysisMode mode)
    {
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, Short, "ocr", NoLines, mode);

        prompt.MaxTokens.Should().Be(2000,
            "code and rich design answers legitimately need room — lower caps truncated the design eval scenario");
    }

    [Theory]
    [InlineData(ScreenAnalysisMode.ExplainCode)]
    [InlineData(ScreenAnalysisMode.MultipleChoice)]
    public void ShortVerbalModes_AnswerAtSpokenLength(ScreenAnalysisMode mode)
    {
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, Short, "ocr", NoLines, mode);

        prompt.MaxTokens.Should().Be(550,
            "a verbal screen answer must be speakable, not documentation-length");
    }

    [Fact]
    public void ShortVerbalModes_StillHonorAnExplicitDeepDiveSetting()
    {
        var deepDive = AnswerSettings.Default with { Length = AnswerLength.DeepDive };

        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, deepDive, "ocr", NoLines, ScreenAnalysisMode.ExplainCode);

        prompt.MaxTokens.Should().Be(2000, "the user explicitly asked for depth");
    }

    [Fact]
    public void ScreenFollowUp_UsesTheSameModeBasedFloor()
    {
        var explain = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, Short, "ocr", ScreenAnalysisMode.ExplainCode, [], [], null);
        var code = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, Short, "ocr", ScreenAnalysisMode.SolveCodingTask, [], [], null);

        explain.MaxTokens.Should().Be(550);
        code.MaxTokens.Should().Be(2000);
    }
}
