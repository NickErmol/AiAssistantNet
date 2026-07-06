using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

/// <summary>
/// <see cref="CaptureScreenHandler"/> must reject captures whose OCR is meeting/window UI chrome
/// (via <see cref="AIHelperNET.Application.Answers.ScreenOcrChromeFilter"/>) so no screen turn is
/// ever created from a Teams title bar. Real task content passes through unchanged.
/// </summary>
public class CaptureScreenChromeRejectionTests
{
    private static CaptureScreenHandler Make(string ocrReturns)
    {
        var ocr = Substitute.For<IScreenOcrService>();
        ocr.CaptureAndReadAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(ocrReturns)));
        return new CaptureScreenHandler(ocr);
    }

    [Fact]
    public async Task ChromeOnlyCapture_IsRejected()
    {
        var handler = Make(
            "Introduction with Mikalai, Vention X Apollo 0 24:05 .11 Mikalai Yarmolenkma tl•dV A1 NOTETAKER Vention Notetaker KS KS K");

        var result = await handler.Handle(new CaptureScreenCommand(), CancellationToken.None);

        result.IsFailed.Should().BeTrue("a captured meeting title bar carries no answerable task");
    }

    [Fact]
    public async Task RealTaskCapture_PassesThrough()
    {
        var handler = Make("Task: implement an LRU cache in C#");

        var result = await handler.Handle(new CaptureScreenCommand(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain("LRU cache");
    }
}
