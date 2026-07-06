using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Application.Reviews.Queries;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Reviews;

/// <summary>Unit tests for <see cref="GetSessionReviewHandler"/>.</summary>
public class GetSessionReviewHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly SessionId AnySessionId = new(Guid.Parse("20000000-0000-0000-0000-000000000001"));

    [Fact]
    public async Task Handle_ReviewExists_ReturnsDtoWithCorrectValues()
    {
        var review = SessionReview.Create(
            AnySessionId,
            "## Questions Asked\nQ1\n## Answer Grades\nG1\n## Detection Audit\nA1\n## Study Topics\nS1",
            "claude-sonnet-4-6",
            T0);

        var reviewRepo = Substitute.For<ISessionReviewRepository>();
        reviewRepo.GetBySessionAsync(AnySessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionReview?>(review));

        var handler = new GetSessionReviewHandler(reviewRepo);
        var result = await handler.Handle(
            new GetSessionReviewQuery(AnySessionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Markdown.Should().Be(review.Markdown);
        result.Value.ModelUsed.Should().Be(review.ModelUsed);
        result.Value.GeneratedAt.Should().Be(review.GeneratedAt);
    }

    [Fact]
    public async Task Handle_NoReviewExists_ReturnsSuccessWithNullValue()
    {
        var reviewRepo = Substitute.For<ISessionReviewRepository>();
        reviewRepo.GetBySessionAsync(AnySessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionReview?>(null));

        var handler = new GetSessionReviewHandler(reviewRepo);
        var result = await handler.Handle(
            new GetSessionReviewQuery(AnySessionId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }
}
