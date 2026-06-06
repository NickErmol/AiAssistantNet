using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Infrastructure.Export;
using Xunit;

namespace AIHelperNET.Infrastructure.Tests.Export;

public sealed class ExportServiceTests
{
    private static SessionDetailDto MakeDetail() => new(
        SessionId.New(),
        new DateTimeOffset(2026, 6, 4, 14, 32, 0, TimeSpan.Zero),
        null,
        "AudioAndScreen",
        [],
        [new("Can you explain CQRS?", "CQRS separates reads and writes.", DateTimeOffset.UtcNow)]);

    [Fact]
    public void ToTxt_ContainsSpeakerAndText()
    {
        var svc    = new ExportService();
        var detail = MakeDetail();

        var txt = svc.ToTxt(detail);

        Assert.Contains("Can you explain CQRS?", txt);
        Assert.Contains("CQRS separates reads and writes.", txt);
    }

    [Fact]
    public void ToMarkdown_ContainsHeadingAndBlockquote()
    {
        var svc    = new ExportService();
        var detail = MakeDetail();

        var md = svc.ToMarkdown(detail);

        Assert.Contains("## Session", md);
        Assert.Contains("### Q:", md);
    }
}
