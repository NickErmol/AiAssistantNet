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

/// <summary>Verifies the SettingsViewModel maps the token cap on load and persists it into the correct
/// (positionally easy-to-mix-up) DTO field on save.</summary>
public class SettingsViewModelTokenTests
{
    private static AppSettingsDto Settings(int tokens) => new AppSettingsDto(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null)
        with { MaxAnswerTokens = tokens };

    [Fact]
    public async Task LoadAsync_MapsMaxAnswerTokens()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(Settings(1500))));
        mediator.Send(Arg.Any<HasApiKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<bool>>(Result.Ok(false)));
#pragma warning restore CA2012
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier());

        await vm.LoadAsync();

        vm.MaxAnswerTokens.Should().Be(1500);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsMaxAnswerTokens_IntoCorrectField()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(Settings(800))));
        mediator.Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier()) { MaxAnswerTokens = 1500 };

        await vm.SaveSettingsAsync();

        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c => c.Settings.MaxAnswerTokens == 1500),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>Verifies the SettingsViewModel maps the Answer-latest window on load and persists it into
/// the correct DTO field on save.</summary>
public class SettingsViewModelWindowTests
{
    private static AppSettingsDto Settings(int windowSeconds) => new AppSettingsDto(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null)
        with { LatestQuestionWindowSeconds = windowSeconds };

    [Fact]
    public async Task LoadAsync_MapsLatestQuestionWindowSeconds()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(Settings(90))));
        mediator.Send(Arg.Any<HasApiKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<bool>>(Result.Ok(false)));
#pragma warning restore CA2012
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier());

        await vm.LoadAsync();

        vm.LatestQuestionWindowSeconds.Should().Be(90);
    }

    [Fact]
    public async Task SaveSettingsAsync_PersistsLatestQuestionWindowSeconds_IntoCorrectField()
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(Settings(120))));
        mediator.Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        var vm = new SettingsViewModel(mediator, new StubHotkeyApplier()) { LatestQuestionWindowSeconds = 200 };

        await vm.SaveSettingsAsync();

        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c => c.Settings.LatestQuestionWindowSeconds == 200),
            Arg.Any<CancellationToken>());
    }
}

/// <summary>No-op applier for VM tests; records the last applied set and can be told which IDs to "fail".</summary>
internal sealed class StubHotkeyApplier : AIHelperNET.App.Hotkeys.IHotkeyApplier
{
    public IReadOnlyList<HotkeyBinding>? LastApplied;
    public int ApplyCalls;
    public IReadOnlyList<HotkeyId> Failures = [];

    public IReadOnlyList<HotkeyId> Apply(IReadOnlyList<HotkeyBinding> bindings)
    {
        ApplyCalls++;
        LastApplied = bindings;
        return Failures;
    }
}
