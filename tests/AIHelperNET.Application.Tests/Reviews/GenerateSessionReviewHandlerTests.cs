using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Application.Reviews.Commands;
using AIHelperNET.Domain.Ids;
using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Reviews;

/// <summary>Unit tests for <see cref="GenerateSessionReviewHandler"/>.</summary>
public class GenerateSessionReviewHandlerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly SessionId AnySessionId = new(Guid.Parse("10000000-0000-0000-0000-000000000001"));

    private static Session MakeSessionWithTranscript()
    {
        var session = Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Other, "What is DI?", T0, 0.9f));
        session.AddTranscriptItem(TranscriptItem.Create(Speaker.Me, "Dependency injection.", T0.AddSeconds(5), 0.9f));
        return session;
    }

    private static Session MakeSessionWithoutTranscript()
        => Session.Create(AnswerSettings.Default, CodeProfile.Empty, T0).Value;

    private static (
        GenerateSessionReviewHandler handler,
        ISessionRepository sessionRepo,
        ISessionReviewRepository reviewRepo,
        ISessionReviewAnalyzer analyzer,
        IUnitOfWork uow,
        FakeTimeProvider clock)
        MakeHandler(Session? session = null)
    {
        var sessionRepo = Substitute.For<ISessionRepository>();
        var reviewRepo = Substitute.For<ISessionReviewRepository>();
        var analyzer = Substitute.For<ISessionReviewAnalyzer>();
        var uow = Substitute.For<IUnitOfWork>();
        var clock = new FakeTimeProvider(T0.AddHours(1));

        var resolved = session ?? MakeSessionWithTranscript();
        sessionRepo.GetAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(resolved)));

        analyzer.AnalyzeAsync(Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Ok(new SessionReviewResult("## Questions Asked\nQ1\n## Answer Grades\nG1\n## Detection Audit\nA1\n## Study Topics\nS1", "claude-sonnet-4-6"))));

        reviewRepo.GetBySessionAsync(Arg.Any<SessionId>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionReview?>(null));

        uow.SaveChangesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok()));

        var handler = new GenerateSessionReviewHandler(sessionRepo, reviewRepo, analyzer, uow, clock);
        return (handler, sessionRepo, reviewRepo, analyzer, uow, clock);
    }

    // ─── Session not found ────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_SessionNotFound_PropagatesFail()
    {
        var (handler, sessionRepo, _, analyzer, _, _) = MakeHandler();
        sessionRepo.GetAsync(AnySessionId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<Session>("Session not found.")));

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(AnySessionId), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await analyzer.DidNotReceive().AnalyzeAsync(
            Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>());
    }

    // ─── Empty transcript guard ───────────────────────────────────────────────

    [Fact]
    public async Task Handle_EmptyTranscript_FailsWithoutCallingAnalyzer()
    {
        var emptySession = MakeSessionWithoutTranscript();
        var (handler, sessionRepo, _, analyzer, _, _) = MakeHandler(emptySession);

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(emptySession.Id), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("no transcript", StringComparison.OrdinalIgnoreCase));
        await analyzer.DidNotReceive().AnalyzeAsync(
            Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>());
    }

    // ─── Analyzer failure ─────────────────────────────────────────────────────

    [Fact]
    public async Task Handle_AnalyzerFails_PropagatesFailWithoutSaving()
    {
        var session = MakeSessionWithTranscript();
        var (handler, _, reviewRepo, analyzer, uow, _) = MakeHandler(session);

        analyzer.AnalyzeAsync(Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<SessionReviewResult>("API error")));

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await reviewRepo.DidNotReceive().AddAsync(Arg.Any<SessionReview>(), Arg.Any<CancellationToken>());
        reviewRepo.DidNotReceive().Update(Arg.Any<SessionReview>());
        await uow.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ─── No existing review → AddAsync path ──────────────────────────────────

    [Fact]
    public async Task Handle_NoExistingReview_CallsAddAsync()
    {
        var session = MakeSessionWithTranscript();
        var (handler, _, reviewRepo, _, uow, _) = MakeHandler(session);

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await reviewRepo.Received(1).AddAsync(Arg.Any<SessionReview>(), Arg.Any<CancellationToken>());
        reviewRepo.DidNotReceive().Update(Arg.Any<SessionReview>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoExistingReview_ReturnedDtoMatchesAnalyzerOutput()
    {
        var session = MakeSessionWithTranscript();
        var (handler, _, reviewRepo, analyzer, uow, clock) = MakeHandler(session);

        const string ExpectedMarkdown =
            "## Questions Asked\nQ1\n## Answer Grades\nG1\n## Detection Audit\nA1\n## Study Topics\nS1";
        const string ExpectedModel = "claude-sonnet-4-6";

        analyzer.AnalyzeAsync(Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(
                Result.Ok(new SessionReviewResult(ExpectedMarkdown, ExpectedModel))));

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markdown.Should().Be(ExpectedMarkdown);
        result.Value.ModelUsed.Should().Be(ExpectedModel);
        result.Value.GeneratedAt.Should().Be(clock.GetUtcNow());
    }

    [Fact]
    public async Task Handle_NoExistingReview_AddedReviewHasCorrectMarkdownAndModel()
    {
        var session = MakeSessionWithTranscript();
        SessionReview? captured = null;
        var (handler, _, reviewRepo, analyzer, _, _) = MakeHandler(session);

        const string Markdown = "## Questions Asked\nQ\n## Answer Grades\nG\n## Detection Audit\nA\n## Study Topics\nS";
        const string Model = "claude-sonnet-4-6";
        analyzer.AnalyzeAsync(Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new SessionReviewResult(Markdown, Model))));

        reviewRepo.AddAsync(Arg.Do<SessionReview>(r => captured = r), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        await handler.Handle(new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.Markdown.Should().Be(Markdown);
        captured.ModelUsed.Should().Be(Model);
        captured.SessionId.Should().Be(session.Id);
    }

    // ─── Existing review → Replace / Update path ─────────────────────────────

    [Fact]
    public async Task Handle_ExistingReview_CallsUpdateNotAddAsync()
    {
        var session = MakeSessionWithTranscript();
        var existingReview = SessionReview.Create(session.Id, "# Old", "claude-haiku", T0);

        var (handler, _, reviewRepo, _, uow, _) = MakeHandler(session);
        reviewRepo.GetBySessionAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionReview?>(existingReview));

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        reviewRepo.Received(1).Update(existingReview);
        await reviewRepo.DidNotReceive().AddAsync(Arg.Any<SessionReview>(), Arg.Any<CancellationToken>());
        await uow.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingReview_ReplacedWithNewMarkdownAndModel()
    {
        var session = MakeSessionWithTranscript();
        var existingReview = SessionReview.Create(session.Id, "# Old", "old-model", T0);

        const string NewMarkdown = "## Questions Asked\nNew\n## Answer Grades\nNew\n## Detection Audit\nNew\n## Study Topics\nNew";
        const string NewModel = "claude-sonnet-4-6";

        var (handler, _, reviewRepo, analyzer, _, _) = MakeHandler(session);
        reviewRepo.GetBySessionAsync(session.Id, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SessionReview?>(existingReview));

        analyzer.AnalyzeAsync(Arg.Any<Application.Answers.AnswerPrompt>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok(new SessionReviewResult(NewMarkdown, NewModel))));

        var result = await handler.Handle(
            new GenerateSessionReviewCommand(session.Id), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Markdown.Should().Be(NewMarkdown);
        result.Value.ModelUsed.Should().Be(NewModel);
        // The review entity itself should have been updated
        existingReview.Markdown.Should().Be(NewMarkdown);
        existingReview.ModelUsed.Should().Be(NewModel);
    }
}
