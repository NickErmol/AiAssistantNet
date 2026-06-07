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
        fixture.Window.Properties.IsOffscreen.ValueOrDefault.Should().BeFalse();
    }

    [Fact]
    public void Log_Contains_SileroVadReady()
    {
        var log = ReadLog();
        log.Should().Contain("Silero", "log should show Silero VAD initialized");
    }

    [Fact]
    public void Log_Contains_WhisperReady()
    {
        var log = ReadLog();
        log.Should().Contain("Whisper", "log should show Whisper model loaded");
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
        fixture.Main.IsDotActive(fixture.Main.DotMic).Should().BeFalse();
        fixture.Main.IsDotActive(fixture.Main.DotSystem).Should().BeFalse();
    }

    private static string ReadLog()
    {
        var path = TodayLogPath;
        path.Should().NotBeNull("today's log file should exist at {0}", path);
        // Open with shared read access — app holds a write lock
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        return reader.ReadToEnd();
    }
}
