using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class SettingsTests(AppFixture fixture)
{
    [Fact]
    public void Settings_WindowOpens_AndContainsTabs()
    {
        fixture.Main.BtnOpenSettings.Click();

        // Wait up to 5 s for the settings window to appear
        var settingsWindow = Retry.WhileNull(
            () => fixture.App.GetAllTopLevelWindows(fixture.Automation)
                .FirstOrDefault(w =>
                    (w.Properties.Name.ValueOrDefault ?? string.Empty)
                    .Contains("Settings", StringComparison.OrdinalIgnoreCase)),
            TimeSpan.FromSeconds(5)).Result;

        settingsWindow.Should().NotBeNull("Settings window should open");

        // Verify tab headers are present (find by content text)
        var allText = settingsWindow.FindAllDescendants()
            .Select(e => e.Properties.Name.ValueOrDefault ?? string.Empty)
            .ToList();

        allText.Should().Contain(t => t.Contains("Audio", StringComparison.OrdinalIgnoreCase),
            "Audio tab should be present");
        allText.Should().Contain(t => t.Contains("Code Profile", StringComparison.OrdinalIgnoreCase) ||
                                      t.Contains("Profile", StringComparison.OrdinalIgnoreCase),
            "Code Profiles tab should be present");
        allText.Should().Contain(t => t.Contains("Appearance", StringComparison.OrdinalIgnoreCase),
            "Appearance tab should be present");

        // Close the settings window
        settingsWindow.Close();
        Thread.Sleep(400);
    }
}
