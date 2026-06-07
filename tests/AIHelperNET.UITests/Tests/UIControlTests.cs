using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class UIControlTests(AppFixture fixture)
{
    // ── Mode radio buttons ────────────────────────────────────────────────────

    [Fact]
    public void Mode_AudioOnly_Selects()
    {
        fixture.Main.RadioModeAudioOnly.Click();
        fixture.Main.RadioModeAudioOnly.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void Mode_ScreenOnly_Selects()
    {
        fixture.Main.RadioModeScreenOnly.Click();
        fixture.Main.RadioModeScreenOnly.IsChecked.Should().BeTrue();
        fixture.Main.RadioModeAudioOnly.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void Mode_AudioAndScreen_Selects()
    {
        fixture.Main.RadioModeAudioScreen.Click();
        fixture.Main.RadioModeAudioScreen.IsChecked.Should().BeTrue();

        // Restore default
        fixture.Main.RadioModeAudioOnly.Click();
    }

    // ── Audio source radio buttons ────────────────────────────────────────────

    [Fact]
    public void AudioSource_MicOnly_Selects()
    {
        fixture.Main.RadioAudioMicOnly.Click();
        fixture.Main.RadioAudioMicOnly.IsChecked.Should().BeTrue();
    }

    [Fact]
    public void AudioSource_SystemOnly_Selects()
    {
        fixture.Main.RadioAudioSystemOnly.Click();
        fixture.Main.RadioAudioSystemOnly.IsChecked.Should().BeTrue();
        fixture.Main.RadioAudioMicOnly.IsChecked.Should().BeFalse();
    }

    [Fact]
    public void AudioSource_Both_Selects()
    {
        fixture.Main.RadioAudioBoth.Click();
        fixture.Main.RadioAudioBoth.IsChecked.Should().BeTrue();

        // Restore default
        fixture.Main.RadioAudioMicOnly.Click();
    }

    // ── Screen mode radio buttons ─────────────────────────────────────────────

    [Theory]
    [InlineData("ScreenMode_General")]
    [InlineData("ScreenMode_SolveCoding")]
    [InlineData("ScreenMode_DebugError")]
    [InlineData("ScreenMode_ExplainCode")]
    [InlineData("ScreenMode_SystemDesign")]
    [InlineData("ScreenMode_MultipleChoice")]
    public void ScreenMode_EachOption_Selects(string automationId)
    {
        var radio = (fixture.Window
            .FindFirstDescendant(cf => cf.ByAutomationId(automationId))
            ?? throw new InvalidOperationException($"Radio button '{automationId}' not found"))
            .AsRadioButton();

        radio.Click();
        radio.IsChecked.Should().BeTrue($"{automationId} should be checked after click");

        // Restore default
        fixture.Main.RadioScreenGeneral.Click();
    }

    // ── Theme toggle ──────────────────────────────────────────────────────────

    [Fact]
    public void ThemeToggle_DoesNotCrash()
    {
        fixture.Main.BtnToggleTheme.Click();
        Thread.Sleep(300);
        fixture.Main.BtnToggleTheme.Click(); // back to dark
        Thread.Sleep(300);
        fixture.Window.Properties.IsOffscreen.ValueOrDefault.Should().BeFalse();
    }

    // ── History panel ─────────────────────────────────────────────────────────

    [Fact]
    public void HistoryPanel_OpensAndCloses()
    {
        fixture.Main.HistoryPanel.Should().BeNull("history panel starts collapsed");

        fixture.Main.BtnToggleHistory.Click();

        var panel = Retry.WhileNull(
            () => fixture.Main.HistoryPanel,
            TimeSpan.FromSeconds(3)).Result;

        panel.Should().NotBeNull("history panel should be visible after clicking the history button");

        fixture.Main.BtnToggleHistory.Click();
        Thread.Sleep(400);

        fixture.Main.HistoryPanel.Should().BeNull("history panel should collapse again");
    }

    // ── Sidebar ───────────────────────────────────────────────────────────────

    [Fact]
    public void Sidebar_HidesAndRestores()
    {
        // BtnHideSidebar is inside the sidebar — its BoundingRectangle.Width is a reliable
        // proxy for sidebar width since WPF clips children when the parent width is 0.
        fixture.Main.BtnHideSidebar.BoundingRectangle.Width
            .Should().BeGreaterThan(0, "sidebar should be visible by default");

        fixture.Main.BtnHideSidebar.Click();
        Thread.Sleep(400);

        fixture.Main.BtnHideSidebar.BoundingRectangle.Width.Should().Be(0,
            "sidebar width should be 0 when hidden");

        // ▶ restore button appears when sidebar is hidden
        fixture.Main.BtnToggleSidebar.Click();
        Thread.Sleep(400);

        fixture.Main.BtnHideSidebar.BoundingRectangle.Width.Should().BeGreaterThan(0,
            "sidebar should be restored");
    }
}
