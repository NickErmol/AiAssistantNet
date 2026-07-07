using AIHelperNET.App.Services;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.App.Tests.Services;

public class OverlayStatusNotifierTests
{
    [Fact]
    public void Notify_SetsMessage_AndRaisesPropertyChanged()
    {
        var sut = new OverlayStatusNotifier();
        var raised = false;
        sut.PropertyChanged += (_, e) => raised |= e.PropertyName == nameof(sut.Message);

        sut.Notify("STT: using local Whisper (Deepgram unavailable)");

        sut.Message.Should().Be("STT: using local Whisper (Deepgram unavailable)");
        raised.Should().BeTrue();
    }
}
