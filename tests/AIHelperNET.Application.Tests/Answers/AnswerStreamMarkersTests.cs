using AIHelperNET.Application.Answers;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

/// <summary>
/// The truncation marker is display-only: it must be visible in the live stream but stripped before
/// the answer is persisted, so it never leaks into the recent-Q&amp;A context of future prompts.
/// </summary>
public class AnswerStreamMarkersTests
{
    [Fact]
    public void StripForStorage_RemovesTrailingTruncationMarker()
        => AnswerStreamMarkers.StripForStorage("Token refresh catches expired or over-" + AnswerStreamMarkers.Truncated)
            .Should().Be("Token refresh catches expired or over-");

    [Fact]
    public void StripForStorage_LeavesCompleteAnswersUntouched()
        => AnswerStreamMarkers.StripForStorage("A complete answer.")
            .Should().Be("A complete answer.");

    [Fact]
    public void StripForStorage_OnlyStripsTheSuffixOccurrence()
    {
        var text = "mentions the phrase mid-text" + AnswerStreamMarkers.Truncated + " and continues";
        AnswerStreamMarkers.StripForStorage(text).Should().Be(text,
            "only a trailing marker is the provider's; mid-text occurrences are model output");
    }
}
