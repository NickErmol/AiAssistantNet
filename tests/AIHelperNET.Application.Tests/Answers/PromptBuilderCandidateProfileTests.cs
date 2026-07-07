using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>
/// Verifies that all four PromptBuilderService overloads correctly inject (or omit)
/// the candidate profile card into the system prompt.
/// </summary>
public class PromptBuilderCandidateProfileTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;

    private const string SampleCard = "**CANDIDATE PROFILE**\nSenior C# developer at Contoso (3 years)";

    // ── Helper calls with no card ──────────────────────────────────────────────────────────────

    private static AnswerPrompt BuildNoCard() =>
        PromptBuilderService.Build(CodeProfile.Empty, AnswerSettings.Default, "What is CQRS?");

    private static AnswerPrompt BuildFollowUpNoCard() =>
        PromptBuilderService.BuildFollowUp(CodeProfile.Empty, AnswerSettings.Default,
            "What is CQRS?", "Prior answer", "Follow-up");

    private static AnswerPrompt BuildWithScreenModeNoCard() =>
        PromptBuilderService.BuildWithScreenMode(CodeProfile.Empty, AnswerSettings.Default,
            "code on screen", ["line"], ScreenAnalysisMode.SolveCodingTask);

    private static AnswerPrompt BuildScreenFollowUpNoCard() =>
        PromptBuilderService.BuildScreenFollowUp(CodeProfile.Empty, AnswerSettings.Default,
            "ocr", ScreenAnalysisMode.SolveCodingTask, ["addition"], [], null);

    // ── Build ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_WithCard_InjectsCardBlockInSystem()
    {
        var prompt = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is CQRS?",
            candidateProfileCard: SampleCard);

        prompt.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        prompt.System.Should().Contain("--- END UNTRUSTED DATA ---");
        prompt.System.Should().Contain(SampleCard);
        prompt.System.Should().Contain("never claim experience beyond it");
    }

    [Fact]
    public void Build_WithCard_BlockAppearsAfterCodeProfileSection()
    {
        var profile = CodeProfile.Empty with { ProgrammingLanguage = "C#" };
        var prompt = PromptBuilderService.Build(
            profile, AnswerSettings.Default, "What is CQRS?",
            candidateProfileCard: SampleCard);

        var codeProfileIdx = prompt.System.IndexOf("Candidate stack", StringComparison.Ordinal);
        var cardFenceIdx   = prompt.System.IndexOf("--- BEGIN UNTRUSTED DATA ---", StringComparison.Ordinal);

        codeProfileIdx.Should().BeGreaterThanOrEqualTo(0, "CodeProfile section should be present");
        cardFenceIdx.Should().BeGreaterThan(codeProfileIdx,
            "card fence must come AFTER the CodeProfile section");
    }

    [Fact]
    public void Build_NullCard_SystemPromptByteIdenticalToNoParam()
    {
        var withNull = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is CQRS?",
            candidateProfileCard: null);
        var noParam = BuildNoCard();

        withNull.System.Should().Be(noParam.System,
            "null card must not alter the system prompt at all");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Build_EmptyOrWhitespaceCard_SystemPromptByteIdenticalToNoParam(string card)
    {
        var withEmpty = PromptBuilderService.Build(
            CodeProfile.Empty, AnswerSettings.Default, "What is CQRS?",
            candidateProfileCard: card);
        var noParam = BuildNoCard();

        withEmpty.System.Should().Be(noParam.System,
            "whitespace/empty card must not alter the system prompt at all");
    }

    // ── BuildFollowUp ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildFollowUp_WithCard_InjectsCardBlockInSystem()
    {
        var prompt = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is CQRS?", "Prior answer", "Follow-up",
            candidateProfileCard: SampleCard);

        prompt.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        prompt.System.Should().Contain("--- END UNTRUSTED DATA ---");
        prompt.System.Should().Contain(SampleCard);
        prompt.System.Should().Contain("never claim experience beyond it");
    }

    [Fact]
    public void BuildFollowUp_NullCard_SystemPromptByteIdenticalToNoParam()
    {
        var withNull = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is CQRS?", "Prior answer", "Follow-up",
            candidateProfileCard: null);
        var noParam = BuildFollowUpNoCard();

        withNull.System.Should().Be(noParam.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildFollowUp_EmptyOrWhitespaceCard_SystemPromptByteIdenticalToNoParam(string card)
    {
        var withEmpty = PromptBuilderService.BuildFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "What is CQRS?", "Prior answer", "Follow-up",
            candidateProfileCard: card);
        var noParam = BuildFollowUpNoCard();

        withEmpty.System.Should().Be(noParam.System);
    }

    // ── BuildWithScreenMode ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildWithScreenMode_WithCard_InjectsCardBlockInSystem()
    {
        var prompt = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default,
            "code on screen", ["line"], ScreenAnalysisMode.SolveCodingTask,
            candidateProfileCard: SampleCard);

        prompt.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        prompt.System.Should().Contain("--- END UNTRUSTED DATA ---");
        prompt.System.Should().Contain(SampleCard);
        prompt.System.Should().Contain("never claim experience beyond it");
    }

    [Fact]
    public void BuildWithScreenMode_NullCard_SystemPromptByteIdenticalToNoParam()
    {
        var withNull = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default,
            "code on screen", ["line"], ScreenAnalysisMode.SolveCodingTask,
            candidateProfileCard: null);
        var noParam = BuildWithScreenModeNoCard();

        withNull.System.Should().Be(noParam.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildWithScreenMode_EmptyOrWhitespaceCard_SystemPromptByteIdenticalToNoParam(string card)
    {
        var withEmpty = PromptBuilderService.BuildWithScreenMode(
            CodeProfile.Empty, AnswerSettings.Default,
            "code on screen", ["line"], ScreenAnalysisMode.SolveCodingTask,
            candidateProfileCard: card);
        var noParam = BuildWithScreenModeNoCard();

        withEmpty.System.Should().Be(noParam.System);
    }

    // ── BuildScreenFollowUp ────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildScreenFollowUp_WithCard_InjectsCardBlockInSystem()
    {
        var prompt = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "ocr", ScreenAnalysisMode.SolveCodingTask, ["addition"], [],
            priorAnswer: null,
            candidateProfileCard: SampleCard);

        prompt.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        prompt.System.Should().Contain("--- END UNTRUSTED DATA ---");
        prompt.System.Should().Contain(SampleCard);
        prompt.System.Should().Contain("never claim experience beyond it");
    }

    [Fact]
    public void BuildScreenFollowUp_NullCard_SystemPromptByteIdenticalToNoParam()
    {
        var withNull = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "ocr", ScreenAnalysisMode.SolveCodingTask, ["addition"], [],
            priorAnswer: null,
            candidateProfileCard: null);
        var noParam = BuildScreenFollowUpNoCard();

        withNull.System.Should().Be(noParam.System);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildScreenFollowUp_EmptyOrWhitespaceCard_SystemPromptByteIdenticalToNoParam(string card)
    {
        var withEmpty = PromptBuilderService.BuildScreenFollowUp(
            CodeProfile.Empty, AnswerSettings.Default,
            "ocr", ScreenAnalysisMode.SolveCodingTask, ["addition"], [],
            priorAnswer: null,
            candidateProfileCard: card);
        var noParam = BuildScreenFollowUpNoCard();

        withEmpty.System.Should().Be(noParam.System);
    }

    // ── Phrasing / fence marker assertions ────────────────────────────────────────────────────

    [Fact]
    public void AllFourBuilders_WithCard_ContainFenceMarkersAndNeverClaimPhrasing()
    {
        var prompts = new[]
        {
            PromptBuilderService.Build(
                CodeProfile.Empty, AnswerSettings.Default, "q",
                candidateProfileCard: SampleCard),
            PromptBuilderService.BuildFollowUp(
                CodeProfile.Empty, AnswerSettings.Default, "q", "a", "f",
                candidateProfileCard: SampleCard),
            PromptBuilderService.BuildWithScreenMode(
                CodeProfile.Empty, AnswerSettings.Default, "ocr", [], ScreenAnalysisMode.SolveCodingTask,
                candidateProfileCard: SampleCard),
            PromptBuilderService.BuildScreenFollowUp(
                CodeProfile.Empty, AnswerSettings.Default, "ocr",
                ScreenAnalysisMode.SolveCodingTask, ["add"], [], null,
                candidateProfileCard: SampleCard),
        };

        foreach (var p in prompts)
        {
            p.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---",
                "all four builders must include the opening fence marker");
            p.System.Should().Contain("--- END UNTRUSTED DATA ---",
                "all four builders must include the closing fence marker");
            p.System.Should().Contain("never claim experience beyond it",
                "all four builders must include the grounding instruction");
        }
    }
}
