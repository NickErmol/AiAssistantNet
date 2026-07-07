using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Application.Sessions.Queries;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using FluentResults;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

/// <summary>
/// Verifies SettingsViewModel Profile tab: file loading, condensation,
/// error handling, re-entry guard, and settings round-trip.
/// </summary>
public class SettingsViewModelProfileTests
{
    // ── helpers ──────────────────────────────────────────────────────

    private static IMediator MockedMediator(AppSettingsDto? settings = null)
    {
        var dto = settings ?? BaseSettings();
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(dto)));
        mediator.Send(Arg.Any<HasApiKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<bool>>(Result.Ok(false)));
        mediator.Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        return mediator;
    }

    private static AppSettingsDto BaseSettings() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    private static SettingsViewModel BuildVm(
        IMediator? mediator = null,
        IDocumentTextExtractor? extractor = null,
        IProfileCondenser? condenser = null,
        Func<string?>? pickFile = null)
    {
        var m = mediator ?? MockedMediator();
        var e = extractor ?? Substitute.For<IDocumentTextExtractor>();
        var c = condenser ?? Substitute.For<IProfileCondenser>();
        var vm = new SettingsViewModel(m, new StubHotkeyApplier(), GlossaryStubs.Empty(), e, c);
        if (pickFile is not null)
            vm.PickFile = pickFile;
        return vm;
    }

    // ── condense success ─────────────────────────────────────────────

    [Fact]
    public async Task CondenseAsync_Success_SetsCardAndClearsError()
    {
        var condenser = Substitute.For<IProfileCondenser>();
        condenser.CondenseAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok("**CANDIDATE PROFILE**\nSenior C# developer")));
        var vm = BuildVm(condenser: condenser);
        vm.ResumeRawText = "Ten years C# experience";
        vm.ProfileErrorMessage = "old error";

        await vm.CondenseCommand.ExecuteAsync(null);

        vm.CandidateProfileCard.Should().Be("**CANDIDATE PROFILE**\nSenior C# developer");
        vm.ProfileErrorMessage.Should().BeEmpty();
        vm.IsCondensing.Should().BeFalse();
    }

    // ── condense Result failure ──────────────────────────────────────

    [Fact]
    public async Task CondenseAsync_ResultFailure_SetsError_CardUntouched()
    {
        var condenser = Substitute.For<IProfileCondenser>();
        condenser.CondenseAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<string>("API error 500")));
        var vm = BuildVm(condenser: condenser);
        vm.ResumeRawText = "Some resume";
        vm.CandidateProfileCard = "existing card";

        await vm.CondenseCommand.ExecuteAsync(null);

        vm.ProfileErrorMessage.Should().Contain("API error 500");
        vm.CandidateProfileCard.Should().Be("existing card");
        vm.IsCondensing.Should().BeFalse();
    }

    // ── condense throwing exception ──────────────────────────────────

    [Fact]
    public async Task CondenseAsync_ThrowsInvalidOperationException_SetsError_DoesNotEscape()
    {
        var condenser = Substitute.For<IProfileCondenser>();
        condenser.CondenseAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<string>>>(_ => Task.FromException<Result<string>>(new InvalidOperationException("boom")));
        var vm = BuildVm(condenser: condenser);
        vm.ResumeRawText = "Some resume";

        // Must not throw out of the method
        Func<Task> act = () => vm.CondenseCommand.ExecuteAsync(null);
        await act.Should().NotThrowAsync();

        vm.ProfileErrorMessage.Should().NotBeEmpty();
        vm.IsCondensing.Should().BeFalse();
    }

    // ── CanCondense gating ───────────────────────────────────────────

    [Fact]
    public void CanCondense_FalseWhenResumeEmpty()
    {
        var vm = BuildVm();
        vm.ResumeRawText = "";
        vm.CondenseCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanCondense_FalseWhenResumeWhitespace()
    {
        var vm = BuildVm();
        vm.ResumeRawText = "   ";
        vm.CondenseCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void CanCondense_TrueWhenResumeHasContent()
    {
        var vm = BuildVm();
        vm.ResumeRawText = "I am a developer";
        vm.CondenseCommand.CanExecute(null).Should().BeTrue();
    }

    // ── re-entrant condense guard ────────────────────────────────────

    [Fact]
    public async Task CondenseAsync_ReEntrant_DoesNotCallCondenserSecondTime()
    {
        // Gate: condenser blocks until we release the TCS
        var tcs = new TaskCompletionSource<Result<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        int callCount = 0;
        var condenser = Substitute.For<IProfileCondenser>();
        condenser.CondenseAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<Task<Result<string>>>(_ =>
            {
                callCount++;
                return tcs.Task;
            });
        var vm = BuildVm(condenser: condenser);
        vm.ResumeRawText = "Some resume";

        // First call — will block
        var first = vm.CondenseCommand.ExecuteAsync(null);

        // Second call while first is still in-flight
        await vm.CondenseCommand.ExecuteAsync(null);

        // Release the first call
        tcs.SetResult(Result.Ok("card"));
        await first;

        callCount.Should().Be(1, "re-entrant call must not invoke the condenser a second time");
    }

    // ── load resume from file — success ──────────────────────────────

    [Fact]
    public async Task LoadResumeFromFileAsync_ExtractorSuccess_SetsResumeRawText()
    {
        var extractor = Substitute.For<IDocumentTextExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok("extracted resume text")));
        var vm = BuildVm(extractor: extractor, pickFile: () => @"C:\resume.pdf");
        vm.ProfileErrorMessage = "old error";

        await vm.LoadResumeFromFileCommand.ExecuteAsync(null);

        vm.ResumeRawText.Should().Be("extracted resume text");
        vm.ProfileErrorMessage.Should().BeEmpty();
    }

    // ── load resume from file — extractor failure ─────────────────────

    [Fact]
    public async Task LoadResumeFromFileAsync_ExtractorFailure_SetsProfileErrorMessage()
    {
        var extractor = Substitute.For<IDocumentTextExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Fail<string>("Unsupported file type")));
        var vm = BuildVm(extractor: extractor, pickFile: () => @"C:\file.xlsx");

        await vm.LoadResumeFromFileCommand.ExecuteAsync(null);

        vm.ProfileErrorMessage.Should().Contain("Unsupported file type");
        vm.ResumeRawText.Should().BeEmpty();
    }

    // ── load resume from file — no file picked ────────────────────────

    [Fact]
    public async Task LoadResumeFromFileAsync_NoFilePicked_DoesNothing()
    {
        var extractor = Substitute.For<IDocumentTextExtractor>();
        var vm = BuildVm(extractor: extractor, pickFile: () => null);

        await vm.LoadResumeFromFileCommand.ExecuteAsync(null);

        await extractor.DidNotReceive().ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        vm.ProfileErrorMessage.Should().BeEmpty();
    }

    // ── load job description from file — success ──────────────────────

    [Fact]
    public async Task LoadJobDescriptionFromFileAsync_ExtractorSuccess_SetsJobDescriptionRawText()
    {
        var extractor = Substitute.For<IDocumentTextExtractor>();
        extractor.ExtractAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Ok("job description text")));
        var vm = BuildVm(extractor: extractor, pickFile: () => @"C:\jd.pdf");

        await vm.LoadJobDescriptionFromFileCommand.ExecuteAsync(null);

        vm.JobDescriptionRawText.Should().Be("job description text");
        vm.ProfileErrorMessage.Should().BeEmpty();
    }

    // ── Save persists all three fields ────────────────────────────────

    [Fact]
    public async Task SaveSettingsAsync_PersistsProfileFieldsToStore()
    {
        var mediator = MockedMediator();
        var vm = BuildVm(mediator: mediator);
        vm.ResumeRawText = "My resume";
        vm.JobDescriptionRawText = "Senior .NET Engineer role";
        vm.CandidateProfileCard = "**CANDIDATE PROFILE**\nSome card";

        await vm.SaveSettingsAsync();

        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c =>
                c.Settings.ResumeRawText == "My resume" &&
                c.Settings.JobDescriptionRawText == "Senior .NET Engineer role" &&
                c.Settings.CandidateProfileCard == "**CANDIDATE PROFILE**\nSome card"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSettingsAsync_WhitespaceFields_StoredAsNull()
    {
        var mediator = MockedMediator();
        var vm = BuildVm(mediator: mediator);
        vm.ResumeRawText = "   ";
        vm.JobDescriptionRawText = "";
        vm.CandidateProfileCard = "   ";

        await vm.SaveSettingsAsync();

        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c =>
                c.Settings.ResumeRawText == null &&
                c.Settings.JobDescriptionRawText == null &&
                c.Settings.CandidateProfileCard == null),
            Arg.Any<CancellationToken>());
    }

    // ── Load populates all three fields ──────────────────────────────

    [Fact]
    public async Task LoadAsync_PopulatesProfileFields()
    {
        var settings = BaseSettings() with
        {
            ResumeRawText = "My resume text",
            JobDescriptionRawText = "JD text",
            CandidateProfileCard = "Profile card"
        };
        var vm = BuildVm(mediator: MockedMediator(settings));

        await vm.LoadAsync();

        vm.ResumeRawText.Should().Be("My resume text");
        vm.JobDescriptionRawText.Should().Be("JD text");
        vm.CandidateProfileCard.Should().Be("Profile card");
    }

    [Fact]
    public async Task LoadAsync_NullProfileFields_MapsToEmptyString()
    {
        var vm = BuildVm(mediator: MockedMediator(BaseSettings()));

        await vm.LoadAsync();

        vm.ResumeRawText.Should().BeEmpty();
        vm.JobDescriptionRawText.Should().BeEmpty();
        vm.CandidateProfileCard.Should().BeEmpty();
    }
}
