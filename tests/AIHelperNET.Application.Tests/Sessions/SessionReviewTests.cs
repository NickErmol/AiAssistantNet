using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SessionReviewTests
{
    private static readonly SessionId SomeSessionId = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));
    private static readonly DateTimeOffset SomeTimestamp = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_SetsAllProperties()
    {
        var review = SessionReview.Create(SomeSessionId, "# Report\nContent here.", "claude-haiku-4-5", SomeTimestamp);

        review.Id.Value.Should().NotBeEmpty();
        review.SessionId.Should().Be(SomeSessionId);
        review.Markdown.Should().Be("# Report\nContent here.");
        review.ModelUsed.Should().Be("claude-haiku-4-5");
        review.GeneratedAt.Should().Be(SomeTimestamp);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsEmptyOrWhitespaceMarkdown(string? markdown)
    {
        var act = () => SessionReview.Create(SomeSessionId, markdown!, "claude-haiku-4-5", SomeTimestamp);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Create_RejectsEmptyOrWhitespaceModelUsed(string? modelUsed)
    {
        var act = () => SessionReview.Create(SomeSessionId, "# Report", modelUsed!, SomeTimestamp);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Replace_OverwritesMarkdownModelUsedAndGeneratedAt_ButKeepsIdAndSessionId()
    {
        var review = SessionReview.Create(SomeSessionId, "# Original", "claude-haiku-4-5", SomeTimestamp);
        var originalId = review.Id;
        var laterTimestamp = SomeTimestamp.AddHours(1);

        review.Replace("# Updated", "claude-sonnet-4-5", laterTimestamp);

        review.Id.Should().Be(originalId);
        review.SessionId.Should().Be(SomeSessionId);
        review.Markdown.Should().Be("# Updated");
        review.ModelUsed.Should().Be("claude-sonnet-4-5");
        review.GeneratedAt.Should().Be(laterTimestamp);
    }
}
