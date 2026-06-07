using FluentAssertions;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class SessionLifecycleTests(AppFixture fixture) : IDisposable
{
    public void Dispose() => StopIfRunning();

    private void StopIfRunning()
    {
        if (fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault == "Stop")
        {
            fixture.Main.BtnToggleSession.Click();
            Thread.Sleep(500);
        }
    }

    [Fact]
    public void Start_HeaderContainsListening()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.SessionStatus.Should().Contain("Listening");

        StopIfRunning();
    }

    [Fact]
    public void Stop_HeaderContainsStopped()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.SessionStatus.Should().Contain("Stopped");
    }

    [Fact]
    public void Start_BothMode_MicAndSystemDotsActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeTrue("Mic dot should be green in Both mode");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeTrue("System dot should be green in Both mode");

        StopIfRunning();
    }

    [Fact]
    public void Start_MicOnly_OnlyMicDotActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioMicOnly.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeTrue("Mic dot should be green in Mic Only mode");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse("System dot should stay grey in Mic Only mode");

        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();
    }

    [Fact]
    public void Start_SystemOnly_OnlySystemDotActive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioSystemOnly.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeTrue("System dot should be green in System Only mode");
        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse("Mic dot should stay grey in System Only mode");

        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();
    }

    [Fact]
    public void Stop_DotsReturnToInactive()
    {
        StopIfRunning();
        fixture.Main.RadioAudioBoth.Click();

        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse("Mic dot should be grey after stop");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse("System dot should be grey after stop");
    }
}
