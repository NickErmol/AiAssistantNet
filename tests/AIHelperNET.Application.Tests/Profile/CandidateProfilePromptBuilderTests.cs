using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Profile;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Profile;

/// <summary>Unit tests for <see cref="CandidateProfilePromptBuilder"/>.</summary>
public class CandidateProfilePromptBuilderTests
{
    private const string SampleResume =
        "John Smith\n\nContoso Corp — Senior Engineer (3 years)\nAzure, C#, microservices\n\nFabrikam — Mid Engineer (2 years)\nSQL Server, .NET";

    private const string SampleJd =
        "## Senior .NET Engineer\n\nRequirements: 5+ years C#, Azure, microservices";

    // ─── System prompt structural requirements ────────────────────────────────

    [Fact]
    public void Build_WithJd_SystemContainsBoldCandidateProfileTitle()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.System.Should().Contain("**CANDIDATE PROFILE**");
    }

    [Fact]
    public void Build_WithJd_SystemContainsBoldTargetRoleTitle()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.System.Should().Contain("**TARGET ROLE**");
    }

    [Fact]
    public void Build_WithoutJd_SystemContainsBoldCandidateProfileTitle()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        prompt.System.Should().Contain("**CANDIDATE PROFILE**");
    }

    [Fact]
    public void Build_WithoutJd_SystemDoesNotRequireTargetRoleBlock()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        // When no JD is provided, the TARGET ROLE output block should not be demanded
        prompt.System.Should().NotContain("**TARGET ROLE**");
    }

    [Fact]
    public void Build_SystemContainsNeverInventGroundingInstruction()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.System.Should().ContainAny(
            "never invent",
            "never invents",
            "do not invent");
    }

    [Fact]
    public void Build_SystemContainsInjectionFenceReference()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.System.Should().Contain("--- BEGIN UNTRUSTED DATA ---");
        prompt.System.Should().Contain("--- END UNTRUSTED DATA ---");
    }

    [Fact]
    public void Build_SystemContainsUntrustedDataInstruction()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.System.Should().ContainAny(
            "UNTRUSTED DATA",
            "untrusted data");
        prompt.System.Should().ContainAny(
            "never obey",
            "never follow");
    }

    [Fact]
    public void Build_SystemSpecifiesNoHashHeadings()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        // The system must explicitly forbid # headings
        prompt.System.Should().ContainAny(
            "no `#`",
            "no # heading",
            "no #",
            "not use #",
            "plain markdown",
            "bold section title");
    }

    // ─── User message — resume block ─────────────────────────────────────────

    [Fact]
    public void Build_UserMessage_WrapsResumeInFenceMarkers()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        var beginIdx = prompt.User.IndexOf("--- BEGIN UNTRUSTED DATA ---", StringComparison.Ordinal);
        var endIdx = prompt.User.IndexOf("--- END UNTRUSTED DATA ---", StringComparison.Ordinal);
        beginIdx.Should().BeGreaterThanOrEqualTo(0, "BEGIN marker must exist");
        endIdx.Should().BeGreaterThan(beginIdx, "END marker must come after BEGIN");

        var fenced = prompt.User[beginIdx..endIdx];
        fenced.Should().Contain("Contoso Corp", "resume text must be inside fence");
    }

    [Fact]
    public void Build_WithJd_UserMessage_WrapsJdInSeparateFenceMarkers()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        // Find all BEGIN/END marker pairs
        var allBegins = new List<int>();
        int idx = 0;
        while ((idx = prompt.User.IndexOf("--- BEGIN UNTRUSTED DATA ---", idx, StringComparison.Ordinal)) >= 0)
        {
            allBegins.Add(idx);
            idx++;
        }

        allBegins.Should().HaveCountGreaterThanOrEqualTo(2, "resume and JD must each be in their own fence block");

        // JD text must be inside one of the fenced regions
        var jdPos = prompt.User.IndexOf("Senior .NET Engineer", StringComparison.Ordinal);
        jdPos.Should().BeGreaterThanOrEqualTo(0, "JD text must appear in user message");

        var jdFenced = allBegins.Any(b =>
        {
            var e = prompt.User.IndexOf("--- END UNTRUSTED DATA ---", b, StringComparison.Ordinal);
            return e > b && jdPos > b && jdPos < e;
        });
        jdFenced.Should().BeTrue("JD text must be inside a BEGIN..END fence");
    }

    [Fact]
    public void Build_WithoutJd_UserMessage_HasSingleFencedBlock()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        // Count BEGIN markers
        var count = 0;
        int idx = 0;
        while ((idx = prompt.User.IndexOf("--- BEGIN UNTRUSTED DATA ---", idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx++;
        }

        count.Should().Be(1, "without JD there should be exactly one fenced block");
    }

    [Fact]
    public void Build_WithoutJd_UserMessage_DoesNotContainJdText()
    {
        const string jd = "This JD should not appear";
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        prompt.User.Should().NotContain(jd);
    }

    // ─── AnswerPrompt metadata ────────────────────────────────────────────────

    [Fact]
    public void Build_MaxTokens_Is1200()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.MaxTokens.Should().Be(1200);
    }

    [Fact]
    public void Build_Model_IsSonnet()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.Model.Should().Be(AnswerModel.Sonnet);
    }

    [Fact]
    public void Build_WithoutJd_MaxTokens_Is1200()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        prompt.MaxTokens.Should().Be(1200);
    }

    [Fact]
    public void Build_WithoutJd_Model_IsSonnet()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, null);

        prompt.Model.Should().Be(AnswerModel.Sonnet);
    }

    [Fact]
    public void Build_UserMessage_ContainsResumeText()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.User.Should().Contain("Contoso Corp");
        prompt.User.Should().Contain("Fabrikam");
    }

    [Fact]
    public void Build_WithJd_UserMessage_ContainsJdText()
    {
        var prompt = CandidateProfilePromptBuilder.Build(SampleResume, SampleJd);

        prompt.User.Should().Contain("Senior .NET Engineer");
    }
}
