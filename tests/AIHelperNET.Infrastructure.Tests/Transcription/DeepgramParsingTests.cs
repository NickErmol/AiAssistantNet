using System.Globalization;
using AIHelperNET.Infrastructure.Transcription.Deepgram;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Transcription;

public class DeepgramParsingTests
{
    // Triple-dollar raw string: the JSON has a run of 3 consecutive literal closing
    // braces ("]}}" at the end of the payload), which a $$ (double-dollar) raw string
    // cannot express unambiguously (CS9007). $$$ makes {{{ }}} the interpolation
    // delimiter, freeing up "}}" to appear as literal content. Numeric values are
    // formatted with InvariantCulture explicitly so this doesn't break on machines
    // whose culture uses a decimal comma.
    private static string ResultsJson(string transcript, float confidence, bool speechFinal, double start) => $$$"""
        {"type":"Results","channel_index":[0,1],"duration":1.0,"start":{{{start.ToString(CultureInfo.InvariantCulture)}}},
         "is_final":true,"speech_final":{{{(speechFinal ? "true" : "false")}}},
         "channel":{"alternatives":[{"transcript":"{{{transcript}}}","confidence":{{{confidence.ToString(CultureInfo.InvariantCulture)}}},"words":[]}]}}
        """;

    [Fact]
    public void Parse_ResultsMessage_ExtractsFields()
    {
        var r = DeepgramResultParser.Parse(ResultsJson("hello world", 0.93f, true, 2.5))!;
        r.Transcript.Should().Be("hello world");
        r.Confidence.Should().BeApproximately(0.93f, 0.001f);
        r.SpeechFinal.Should().BeTrue();
        r.StartSeconds.Should().BeApproximately(2.5, 0.001);
    }

    [Theory]
    [InlineData("""{"type":"Metadata","request_id":"x"}""")]
    [InlineData("""{"type":"SpeechStarted"}""")]
    [InlineData("not json at all")]
    [InlineData("""{"type":"Results"}""")]
    public void Parse_NonResultsOrMalformed_ReturnsNull(string json)
        => DeepgramResultParser.Parse(json).Should().BeNull();

    [Fact]
    public void Assembler_JoinsChunksUntilSpeechFinal()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("yeah so my credit card number is two two", 0.9f, false, 0.0))
            .Should().BeNull();
        var utt = sut.Add(new DeepgramResult("two two three three", 0.8f, true, 3.26))!;
        utt.Text.Should().Be("yeah so my credit card number is two two two two three three");
        utt.Confidence.Should().BeApproximately(0.85f, 0.001f);   // average of chunk confidences
        utt.StartSeconds.Should().BeApproximately(0.0, 0.001);    // first chunk's start
    }

    [Fact]
    public void Assembler_EmptySpeechFinal_YieldsNothing_AndResets()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("", 0f, true, 1.0)).Should().BeNull();
        var utt = sut.Add(new DeepgramResult("next utterance starts clean", 0.9f, true, 5.0))!;
        utt.StartSeconds.Should().BeApproximately(5.0, 0.001);
    }

    [Fact]
    public void Assembler_ResetsAfterEmit()
    {
        var sut = new DeepgramUtteranceAssembler();
        sut.Add(new DeepgramResult("first", 0.9f, true, 0.0));
        var second = sut.Add(new DeepgramResult("second utterance here", 0.7f, true, 9.9))!;
        second.Text.Should().Be("second utterance here");
        second.Confidence.Should().BeApproximately(0.7f, 0.001f);
    }
}
