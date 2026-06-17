using AIHelperNET.Infrastructure.Transcription;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public sealed class TranscriptHallucinationFilterTests
{
    [Theory]
    [InlineData("thank you")]
    [InlineData("Thank you.")]
    [InlineData("- Thank you.")]                                   // leading dash (the bug)
    [InlineData("Thanks for watching!")]
    [InlineData("please subscribe")]
    [InlineData("(audio cuts out)")]                               // bracketed annotation
    [InlineData("[Music]")]
    [InlineData("- I'll see you next time. - I'll see you next time. - Bye.")] // repeated fillers + dashes
    public void Hallucinations_AreDetected(string text)
    {
        Assert.True(TranscriptHallucinationFilter.IsHallucination(text), text);
    }

    [Theory]
    [InlineData("Can you explain dependency injection?")]
    [InlineData("How would you store secrets using Azure Key Vault?")]
    [InlineData("Write a small example of a repository interface.")]
    [InlineData("Explain the difference (audio cuts out) between Task and ValueTask.")] // real content w/ annotation embedded
    [InlineData("")]
    [InlineData("   ")]
    public void RealSpeech_IsNotDetected(string text)
    {
        Assert.False(TranscriptHallucinationFilter.IsHallucination(text), text);
    }
}
