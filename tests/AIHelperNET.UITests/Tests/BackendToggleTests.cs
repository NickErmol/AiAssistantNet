using FluentAssertions;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class BackendToggleTests(AppFixture fixture)
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private Window OpenSettingsAndWait()
    {
        // Invoke via the UIA InvokePattern rather than a coordinate-based Click: when a prior test in
        // the shared app instance leaves another window foreground (e.g. the screen-capture image
        // viewer), a mouse-coordinate click lands on that window and the Settings window never opens.
        // The pattern raises the button's Click regardless of z-order/foreground. Re-invoke on each
        // poll in case the first invoke is swallowed while focus is still settling.
        var win = Retry.WhileNull(
            () =>
            {
                fixture.Main.BtnOpenSettings.Invoke();
                return fixture.App.GetAllTopLevelWindows(fixture.Automation)
                    .FirstOrDefault(w =>
                        (w.Properties.Name.ValueOrDefault ?? string.Empty)
                        .Contains("Settings", StringComparison.OrdinalIgnoreCase));
            },
            TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(500)).Result;

        win.Should().NotBeNull("Settings window should open");

        // Allow Window_Loaded / LoadAsync to complete
        Thread.Sleep(800);

        return win!;
    }

    private static RadioButton GetRadio(Window win, string automationId)
    {
        var element = Retry.WhileNull(
            () => win.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(3)).Result;

        element.Should().NotBeNull($"Radio button '{automationId}' must be present in the Settings window");
        return element!.AsRadioButton();
    }

    private static Button GetSaveButton(Window win)
    {
        var element = Retry.WhileNull(
            () => win.FindFirstDescendant(cf => cf.ByAutomationId("Btn_SaveSettings")),
            TimeSpan.FromSeconds(3)).Result;

        element.Should().NotBeNull("Save button (Btn_SaveSettings) must exist in the Settings window");
        return element!.AsButton();
    }

    /// <summary>
    /// Selects a radio button via SelectionItemPattern.Select() rather than a simulated mouse click.
    /// This is necessary because the Settings window may open on a secondary monitor, where
    /// coordinate-based mouse simulation lands outside the visible area and silently fails.
    /// </summary>
    private static void SelectRadio(Window win, string automationId)
    {
        var element = Retry.WhileNull(
            () => win.FindFirstDescendant(cf => cf.ByAutomationId(automationId)),
            TimeSpan.FromSeconds(3)).Result;

        element.Should().NotBeNull($"Radio button '{automationId}' must be present");

        // Use the UIA SelectionItemPattern directly on the AutomationElement —
        // works regardless of window position on screen (no mouse coordinates).
        element!.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(200); // let WPF two-way binding propagate to ViewModel
    }

    // ── Test ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the Ollama backend in Settings, saves, re-opens Settings,
    /// and asserts the selection was persisted. Restores Claude afterward.
    /// </summary>
    [Fact]
    public void BackendSelection_Ollama_PersistsAcrossSettingsReopen()
    {
        // ── Step 1: open Settings (API Key tab is first — no tab switch needed) ──
        var settingsWindow = OpenSettingsAndWait();

        // Verify starting state: Claude should be the default
        GetRadio(settingsWindow, "Backend_Claude").IsChecked.Should().BeTrue(
            "Claude should be the default backend on first open");

        Window? settingsWindow2 = null;
        try
        {
            // ── Step 2: select Ollama via UIA pattern (not mouse click) ──────────
            SelectRadio(settingsWindow, "Backend_Ollama");

            GetRadio(settingsWindow, "Backend_Ollama").IsChecked.Should().BeTrue(
                "Ollama radio should be checked after SelectionItemPattern.Select()");

            // ── Step 3: save and allow the async command to complete ──────────────
            GetSaveButton(settingsWindow).Click();
            Thread.Sleep(1000); // wait for async SaveSettingsCommand / JSON write to complete

            settingsWindow.Close(); // SettingsWindow.OnClosing cancels and calls Hide() (singleton)
            Thread.Sleep(600);

            // ── Step 4: re-open Settings and assert persistence ───────────────────
            // The SettingsWindow is a singleton (DI AddSingleton). If Window_Loaded re-fires on
            // Show(), LoadAsync reloads from the saved JSON; otherwise the ViewModel retains its
            // state. Either way, Backend_Ollama must be checked.
            settingsWindow2 = OpenSettingsAndWait();

            GetRadio(settingsWindow2, "Backend_Ollama").IsChecked.Should().BeTrue(
                "Ollama backend selection should persist after closing and reopening Settings");
        }
        finally
        {
            // ── Step 5: restore default (Claude) and save ─────────────────────────
            // Always runs — prevents settings-state pollution when an assertion above fails.
            var restoreWindow = settingsWindow2 ?? OpenSettingsAndWait();
            SelectRadio(restoreWindow, "Backend_Claude");

            GetSaveButton(restoreWindow).Click();
            Thread.Sleep(1000); // wait for async SaveSettingsCommand / JSON write to complete

            restoreWindow.Close();
            Thread.Sleep(400);
        }
    }
}
