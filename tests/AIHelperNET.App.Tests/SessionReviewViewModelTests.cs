using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Reviews;
using AIHelperNET.Application.Reviews.Commands;
using AIHelperNET.Application.Reviews.Queries;
using AIHelperNET.Domain.Ids;
using FluentAssertions;
using FluentResults;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

public class SessionReviewViewModelTests
{
    private static readonly SessionId SomeSessionId = SessionId.New();
    private static readonly SessionReviewDto SomeReview = new("## Hello\nworld", "claude-sonnet-4-5", DateTimeOffset.UtcNow);

    private static (SessionReviewViewModel vm, IMediator mediator) Build()
    {
        var mediator = Substitute.For<IMediator>();
        var vm = new SessionReviewViewModel(mediator);
        vm.Initialize(SomeSessionId);
        return (vm, mediator);
    }

    // ── 1. persisted review exists → show it, no GenerateSessionReviewCommand sent ──────────

    [Fact]
    public async Task LoadAsync_WhenPersistedReviewExists_ShowsItWithoutGenerating()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(SomeReview)));
#pragma warning restore CA2012

        await vm.LoadAsync();

        vm.Markdown.Should().Be(SomeReview.Markdown);
        vm.ModelUsed.Should().Be(SomeReview.ModelUsed);
        vm.ErrorMessage.Should().BeNullOrEmpty();
        await mediator.DidNotReceive().Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenPersistedReviewExists_SetsGeneratedAtLabel()
    {
        var (vm, mediator) = Build();
        var generatedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
        var review = SomeReview with { GeneratedAt = generatedAt };
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(review)));
#pragma warning restore CA2012

        await vm.LoadAsync();

        vm.GeneratedAtLabel.Should().NotBeNullOrEmpty();
    }

    // ── 2. no persisted review → generate command sent, markdown set ─────────────────────────

    [Fact]
    public async Task LoadAsync_WhenNoPersistedReview_SendsGenerateCommand()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(null)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Ok(SomeReview)));
#pragma warning restore CA2012

        await vm.LoadAsync();

        await mediator.Received(1).Send(
            Arg.Is<GenerateSessionReviewCommand>(c => c.SessionId == SomeSessionId),
            Arg.Any<CancellationToken>());
        vm.Markdown.Should().Be(SomeReview.Markdown);
        vm.ErrorMessage.Should().BeNullOrEmpty();
    }

    // ── 3. generate failure → ErrorMessage set, Markdown untouched ───────────────────────────

    [Fact]
    public async Task LoadAsync_WhenGenerateFails_SetsErrorMessage_MarkdownUntouched()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(null)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Fail<SessionReviewDto>("API error")));
#pragma warning restore CA2012

        await vm.LoadAsync();

        vm.ErrorMessage.Should().Contain("API error");
        vm.Markdown.Should().BeNullOrEmpty();
    }

    // ── 4. RegenerateAsync always sends generate command, replaces Markdown on success ────────

    [Fact]
    public async Task RegenerateAsync_AlwaysSendsGenerateCommand_ReplacesMarkdown()
    {
        var (vm, mediator) = Build();
        var updated = SomeReview with { Markdown = "## Updated" };
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(SomeReview)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Ok(updated)));
#pragma warning restore CA2012

        await vm.LoadAsync(); // populates with SomeReview

        await vm.RegenerateCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<GenerateSessionReviewCommand>(c => c.SessionId == SomeSessionId),
            Arg.Any<CancellationToken>());
        vm.Markdown.Should().Be(updated.Markdown);
        vm.ErrorMessage.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task RegenerateAsync_OnFailure_SetsErrorMessage()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(SomeReview)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Fail<SessionReviewDto>("Quota exceeded")));
#pragma warning restore CA2012

        await vm.LoadAsync();
        await vm.RegenerateCommand.ExecuteAsync(null);

        vm.ErrorMessage.Should().Contain("Quota exceeded");
    }

    // ── 5. Cancellation → OperationCanceledException swallowed silently ──────────────────────

    [Fact]
    public async Task LoadAsync_WhenCancelled_SilentlySwallows_NoErrorMessage()
    {
        var mediator = Substitute.For<IMediator>();
        var vm = new SessionReviewViewModel(mediator);
        vm.Initialize(SomeSessionId);
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callInfo.ArgAt<CancellationToken>(1).ThrowIfCancellationRequested();
                return new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(null));
            });
#pragma warning restore CA2012

        vm.Cancel(); // cancel before load runs
        await vm.LoadAsync();

        vm.ErrorMessage.Should().BeNullOrEmpty();
    }

    // ── 6. IsLoading toggles around generation ────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_IsLoading_FalseAfterCompletion()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(null)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Ok(SomeReview)));
#pragma warning restore CA2012

        await vm.LoadAsync();

        vm.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task RegenerateAsync_IsLoading_FalseAfterCompletion()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(SomeReview)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto>>(Result.Ok(SomeReview)));
#pragma warning restore CA2012

        await vm.LoadAsync();
        await vm.RegenerateCommand.ExecuteAsync(null);

        vm.IsLoading.Should().BeFalse();
    }

    // ── Finding 2: exception safety ───────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_WhenMediatorThrows_SetsErrorMessage_DoesNotRethrow()
    {
        var (vm, mediator) = Build();
        mediator.When(m => m.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("DB exploded"));

        var act = () => vm.LoadAsync();
        await act.Should().NotThrowAsync();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task RegenerateAsync_WhenMediatorThrows_SetsErrorMessage_DoesNotRethrow()
    {
        var (vm, mediator) = Build();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(SomeReview)));
#pragma warning restore CA2012
        mediator.When(m => m.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("Network exploded"));

        await vm.LoadAsync();
        var act = () => vm.RegenerateAsync();
        await act.Should().NotThrowAsync();
        vm.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    // ── Finding 4: concurrent regenerate guard ────────────────────────────────

    [Fact]
    public async Task RegenerateAsync_WhileLoading_DoesNotSendSecondGenerateCommand()
    {
        var (vm, mediator) = Build();
        var tcs = new TaskCompletionSource<Result<SessionReviewDto>>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<SessionReviewDto?>>(Result.Ok<SessionReviewDto?>(null)));
        mediator.Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new ValueTask<Result<SessionReviewDto>>(tcs.Task));
#pragma warning restore CA2012

        var loadTask = vm.LoadAsync(); // starts, IsLoading = true, waiting on tcs
        // IsLoading should be true now
        vm.IsLoading.Should().BeTrue();
        // Attempt to regenerate while loading — should return immediately without sending
        await vm.RegenerateAsync();
        // Complete the first load
        tcs.SetResult(Result.Ok(SomeReview));
        await loadTask;
        // Only 1 Send for GenerateSessionReviewCommand
        await mediator.Received(1).Send(Arg.Any<GenerateSessionReviewCommand>(), Arg.Any<CancellationToken>());
    }

    // ── Finding 5: IsLoading true during DB query ────────────────────────────

    [Fact]
    public async Task LoadAsync_IsLoading_TrueDuringQueryRoundTrip()
    {
        var (vm, mediator) = Build();
        var tcs = new TaskCompletionSource<Result<SessionReviewDto?>>();
        bool? loadingDuringQuery = null;
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSessionReviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                loadingDuringQuery = vm.IsLoading;
                return new ValueTask<Result<SessionReviewDto?>>(tcs.Task);
            });
#pragma warning restore CA2012

        var loadTask = vm.LoadAsync();
        tcs.SetResult(Result.Ok<SessionReviewDto?>(SomeReview));
        await loadTask;

        loadingDuringQuery.Should().BeTrue();
        vm.IsLoading.Should().BeFalse();
    }
}

public class HistoryViewModelReviewRequestedTests
{
    [Fact]
    public void ReviewCommand_RaisesReviewRequestedWithCorrectSessionId()
    {
        var mediator = Substitute.For<IMediator>();
        var vm = new HistoryViewModel(mediator);

        SessionId? raisedId = null;
        vm.ReviewRequested += id => raisedId = id;

        var dto = new AIHelperNET.Application.Sessions.Dtos.SessionSummaryDto(
            SessionId.New(),
            DateTimeOffset.UtcNow,
            null,
            AIHelperNET.Domain.Sessions.SessionState.Stopped,
            0,
            0);
        var sessionVm = new SessionSummaryVm(dto);

        vm.ReviewCommand.Execute(sessionVm);

        raisedId.Should().Be(sessionVm.Id);
    }
}
