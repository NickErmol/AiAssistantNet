using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Answers;
using FluentAssertions;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

public class RecentCaptureRingBufferTests
{
    private static ConversationTurnViewModel NewVm() =>
        new(Substitute.For<IMediator>(), TimeProvider.System, new ScreenTaskContextStore());

    [Fact]
    public void KeepsOnlyLastTwoCaptures_NewestLast()
    {
        var vm = NewVm();
        var t0 = DateTimeOffset.UnixEpoch;

        vm.RecordCaptureForTest("one",   t0);
        vm.RecordCaptureForTest("two",   t0.AddSeconds(1));
        vm.RecordCaptureForTest("three", t0.AddSeconds(2));

        var snap = vm.RecentCaptureSnapshot();
        snap.Should().HaveCount(2);
        snap[0].Ocr.Should().Be("two");
        snap[1].Ocr.Should().Be("three");
    }
}
