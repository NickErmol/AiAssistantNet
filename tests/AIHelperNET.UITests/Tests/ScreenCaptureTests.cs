using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class ScreenCaptureTests(AppFixture fixture, Xunit.Abstractions.ITestOutputHelper output) : IDisposable
{
    private System.Diagnostics.Process? _imageProcess;

    public void Dispose()
    {
        _imageProcess?.Kill();
        _imageProcess?.Dispose();

        // Opening a .png via the shell (UseShellExecute) launches the Windows Photos viewer in its
        // own process; the handle returned by Process.Start is only the shell launcher, so killing
        // _imageProcess leaves the Photos window open. Explicitly close any Photos viewer still
        // showing the test image so it doesn't linger after the test run.
        CloseImageViewer("coding_question");

        // Stop session if running
        if (fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault == "Stop")
        {
            // Use InvokePattern so the click works regardless of which window has OS focus.
            fixture.Main.BtnToggleSession.Patterns.Invoke.Pattern.Invoke();
            Thread.Sleep(500);
        }
    }

    /// <summary>
    /// Closes any Windows Photos viewer process whose main window title contains
    /// <paramref name="titleFragment"/> (e.g. the opened image's file name). Best-effort: failures
    /// to enumerate or kill a process are swallowed so teardown never fails the test.
    /// </summary>
    private static void CloseImageViewer(string titleFragment)
    {
        foreach (var proc in System.Diagnostics.Process.GetProcessesByName("Photos"))
        {
            try
            {
                if (proc.MainWindowTitle.Contains(titleFragment, StringComparison.OrdinalIgnoreCase))
                    proc.Kill();
            }
            catch
            {
                // Process may have already exited, or access denied — ignore during teardown.
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    [Fact]
    public void Capture_WithTestImage_ProducesTurnCard()
    {
        // Ensure Screen Only mode so capture triggers an answer.
        // Use SelectionItemPattern.Select() rather than Click() — after the test image is
        // opened in the Photos viewer (below), the OS foreground focus shifts to Photos.
        // FlaUI Click() uses mouse-coordinate simulation, which is silently dropped when
        // the AIHelper overlay does not own the foreground focus.  SelectionItemPattern /
        // InvokePattern work through the accessibility API and require no focus.
        fixture.Main.RadioModeScreenOnly.Patterns.SelectionItem.Pattern.Select();
        Thread.Sleep(200);

        // Open the test image so there is something on screen to OCR
        // BaseDirectory = tests/AIHelperNET.UITests/bin/Debug/net10.0-.../
        // 5 levels up = solution root D:\work\AIHelperNET
        var imagePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory,
                @"..\..\..\..\..\tests\testImage\coding_question.png"));

        _imageProcess = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = imagePath,
            UseShellExecute = true,
        });
        Thread.Sleep(1500); // allow image viewer to open

        // Start a session.  Use InvokePattern — same reason as the radio button above.
        fixture.Main.BtnToggleSession.Patterns.Invoke.Pattern.Invoke();

        // Wait deterministically for the session to start (button → "Stop").
        Retry.WhileTrue(
            () => fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault != "Stop",
            TimeSpan.FromSeconds(5));

        output.WriteLine($"[DIAG] After start wait: SessionBtn={fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault}");

        // Click the Capture button (InvokePattern — window still may not have focus).
        fixture.Main.BtnCapture.Patterns.Invoke.Pattern.Invoke();

        // Wait up to 30 s for a turn card to appear.
        // A turn card is created synchronously (before streaming) by OnTurnCreated, so it
        // should appear within a few seconds regardless of whether the AI backend succeeds.
        FlaUI.Core.AutomationElements.AutomationElement? turnCard = null;
        for (int i = 0; i < 60; i++)
        {
            Thread.Sleep(500);
            turnCard = fixture.Main.FirstTurnCard;
            var allCards = fixture.Window.FindAllDescendants(cf => cf.ByAutomationId("TurnCard"));
            var allElements = fixture.Window.FindAllDescendants();
            output.WriteLine($"[DIAG] Poll {i+1}/60: FirstTurnCard={turnCard is not null}, FindAll count={allCards.Length}, AllDescendants={allElements.Length}, SessionBtn={fixture.Main.BtnToggleSession.Properties.Name.ValueOrDefault}");
            if (turnCard is not null) break;
        }

        turnCard.Should().NotBeNull("a turn card should appear after screen capture");
    }
}
