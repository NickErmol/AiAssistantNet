using FluentAssertions;
using FlaUI.Core.Tools;
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

    /// <summary>
    /// Waits (up to <paramref name="timeout"/>) for the toggle button to show "Start", which
    /// confirms that <c>ToggleSessionAsync</c>'s async stop path has fully settled
    /// (IsSessionActive=false, IsMicActive=false, IsSystemAudioActive=false all written).
    /// </summary>
    private void WaitUntilStopped(TimeSpan timeout)
        => Retry.WhileTrue(
            () => fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault == "Stop",
            timeout);

    /// <summary>
    /// Selects the "Both" audio-source radio button using the UIA SelectionItemPattern
    /// rather than a simulated mouse click.  RadioButton.Click() in FlaUI sends a
    /// coordinate-based mouse event, which is silently dropped when the overlay window
    /// does not own the foreground focus — leaving the ViewModel's AudioSource unchanged.
    /// SelectionItemPattern.Select() goes through the accessibility API and works
    /// regardless of focus.
    /// </summary>
    private void SelectAudioSourceBoth()
    {
        fixture.Main.RadioAudioBoth.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(200); // let WPF two-way binding propagate to ViewModel
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

        // Wait for the async stop to fully settle before changing audio source.
        // StopIfRunning() only sleeps 500 ms but ToggleSessionAsync (stop path) awaits
        // runner.StopAsync(); IsSessionActive / IsMicActive / IsSystemAudioActive are only
        // written AFTER that completes. Polling for "Start" guarantees we are in a clean
        // stopped state before we restore AudioSource=Both.
        WaitUntilStopped(TimeSpan.FromSeconds(5));

        // Use UIA SelectionItemPattern.Select() instead of Click() — see SelectAudioSourceBoth()
        // doc comment for why Click() is unreliable here.
        SelectAudioSourceBoth();

        fixture.Main.BtnToggleSession.Click();

        // Both audio captures initialize concurrently — system audio can lag behind mic.
        // Poll up to 5 s for each dot rather than using a fixed sleep.
        Retry.WhileTrue(() => !fixture.Main.IsDotActive(fixture.Main.DotMic),
            TimeSpan.FromSeconds(5));
        Retry.WhileTrue(() => !fixture.Main.IsDotActive(fixture.Main.DotSystem),
            TimeSpan.FromSeconds(5));

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
        // Restore AudioSource=Both via UIA SelectionItemPattern — Click() is unreliable when the
        // overlay window does not hold foreground focus (see SelectAudioSourceBoth()).
        SelectAudioSourceBoth();
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
        // Restore AudioSource=Both via UIA SelectionItemPattern — Click() is unreliable when the
        // overlay window does not hold foreground focus (see SelectAudioSourceBoth()).
        SelectAudioSourceBoth();
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
