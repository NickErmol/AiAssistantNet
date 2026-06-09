using FluentAssertions;
using FlaUI.Core.Tools;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class ScreenCaptureTests(AppFixture fixture) : IDisposable
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
            fixture.Main.BtnToggleSession.Click();
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
        // Ensure Screen Only mode so capture triggers an answer
        fixture.Main.RadioModeScreenOnly.Click();
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

        // Start a session
        fixture.Main.BtnToggleSession.Click();
        Thread.Sleep(600);

        // Click the Capture button
        fixture.Main.BtnCapture.Click();

        // Wait up to 30 s for a turn card to appear
        var turnCard = Retry.WhileNull(
            () => fixture.Main.FirstTurnCard,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500)).Result;

        turnCard.Should().NotBeNull("a turn card should appear after screen capture");
    }
}
