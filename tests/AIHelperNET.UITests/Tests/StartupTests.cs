using FluentAssertions;
using Xunit;

namespace AIHelperNET.UITests.Tests;

[Collection("UITests")]
public sealed class StartupTests(AppFixture fixture)
{
    private static string TodayLogPath =>
        Path.Combine(
            Directory.Exists(@"D:\AIHelperNET\logs") ? @"D:\AIHelperNET\logs" :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIHelperNET", "logs"),
            $"log-{DateTime.Now:yyyyMMdd}.txt");

    [Fact]
    public void App_Window_IsPresent()
    {
        fixture.Window.Should().NotBeNull();
        fixture.Window.Properties.IsOffscreen.ValueOrDefault.Should().BeFalse("main overlay window should be visible on screen");
    }

    [Fact]
    public void Log_Contains_SileroVadReady()
    {
        WaitForLogContent("Silero", TimeSpan.FromSeconds(30))
            .Should().BeTrue("log should show Silero VAD initialized within 30s");
    }

    [Fact]
    public void Log_Contains_WhisperReady()
    {
        WaitForLogContent("Whisper", TimeSpan.FromSeconds(30))
            .Should().BeTrue("log should show Whisper model loaded within 30s");
    }

    [Fact]
    public void StatusDot_OCR_IsActive_AtStartup()
    {
        fixture.Main.IsDotActive(fixture.Main.DotOCR).Should().BeTrue(
            "OCR dot should be green at startup");
    }

    [Fact]
    public void StatusDots_Mic_And_System_AreInactive_BeforeSession()
    {
        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse("Mic dot should be grey before session starts");
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse("System dot should be grey before session starts");
    }

    private static bool WaitForLogContent(string marker, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var path = TodayLogPath;
            if (File.Exists(path))
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(fs);
                if (reader.ReadToEnd().Contains(marker, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            Thread.Sleep(500);
        }
        return false;
    }
}
