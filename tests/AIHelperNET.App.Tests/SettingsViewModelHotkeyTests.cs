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

public class SettingsViewModelHotkeyTests
{
    private static IMediator Mocked(AppSettingsDto settings)
    {
        var mediator = Substitute.For<IMediator>();
#pragma warning disable CA2012
        mediator.Send(Arg.Any<GetSettingsQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<AppSettingsDto>>(Result.Ok(settings)));
        mediator.Send(Arg.Any<HasApiKeyQuery>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result<bool>>(Result.Ok(false)));
        mediator.Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<Result>(Result.Ok()));
#pragma warning restore CA2012
        return mediator;
    }

    private static AppSettingsDto BaseSettings() => new(
        AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null);

    [Fact]
    public async Task LoadAsync_BuildsOneRowPerAction_WithOverrideApplied()
    {
        var settings = BaseSettings() with
        {
            HotkeyOverrides = [new HotkeyOverride(HotkeyId.GenerateAnswer,
                ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G)]
        };
        var vm = new SettingsViewModel(Mocked(settings), new StubHotkeyApplier());

        await vm.LoadAsync();

        vm.HotkeyRows.Should().HaveCount(Enum.GetValues<HotkeyId>().Length);
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer).Gesture.Should().Be("Ctrl+Alt+G");
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CopyAnswer).Gesture.Should().Be("Ctrl+Shift+C");
    }

    [Fact]
    public async Task SaveSettingsAsync_WithDuplicateChord_ShowsErrors_DoesNotPersistOrApply()
    {
        var mediator = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier();
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        var copy = vm.HotkeyRows.Single(r => r.Id == HotkeyId.CopyAnswer);
        copy.SetChord(ModifierKeys.Ctrl | ModifierKeys.Shift, VirtualKey.Q); // collide with GenerateAnswer

        await vm.SaveSettingsAsync();

        copy.ErrorMessage.Should().NotBeNullOrEmpty();
        applier.ApplyCalls.Should().Be(0);
        await mediator.DidNotReceive().Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSettingsAsync_Clean_PersistsOverrides_AndApplies()
    {
        var mediator = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier();
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer)
            .SetChord(ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G);

        await vm.SaveSettingsAsync();

        applier.ApplyCalls.Should().Be(1);
        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c =>
                c.Settings.HotkeyOverrides.Count == 1 &&
                c.Settings.HotkeyOverrides[0].Id == HotkeyId.GenerateAnswer &&
                c.Settings.HotkeyOverrides[0].Key == VirtualKey.G),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveSettingsAsync_WhenApplierReportsConflict_ShowsRowError_AndReverts()
    {
        var mediator = Mocked(BaseSettings());
        var applier = new StubHotkeyApplier { Failures = [HotkeyId.GenerateAnswer] };
        var vm = new SettingsViewModel(mediator, applier);
        await vm.LoadAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer)
            .SetChord(ModifierKeys.Ctrl | ModifierKeys.Alt, VirtualKey.G);

        await vm.SaveSettingsAsync();

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer).ErrorMessage.Should().NotBeNullOrEmpty();
        applier.ApplyCalls.Should().Be(2); // attempt + revert to last-good
        await mediator.DidNotReceive().Send(Arg.Any<SaveSettingsCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetRow_RestoresDefault_ResetAll_RestoresEverything()
    {
        var vm = new SettingsViewModel(Mocked(BaseSettings()), new StubHotkeyApplier());
        await vm.LoadAsync();

        var gen = vm.HotkeyRows.Single(r => r.Id == HotkeyId.GenerateAnswer);
        gen.SetChord(ModifierKeys.Win, VirtualKey.J);
        vm.ResetRowCommand.Execute(gen);
        gen.Gesture.Should().Be("Ctrl+Shift+Q");

        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CaptureScreen).SetChord(ModifierKeys.Win, VirtualKey.K);
        vm.ResetAllHotkeysCommand.Execute(null);
        vm.HotkeyRows.Single(r => r.Id == HotkeyId.CaptureScreen).Gesture.Should().Be("Ctrl+Shift+S");
    }
}
