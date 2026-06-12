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
        var vm = new SettingsViewModel(mediator);

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
        var vm = new SettingsViewModel(mediator) { MaxAnswerTokens = 1500 };

        await vm.SaveSettingsAsync();

        await mediator.Received(1).Send(
            Arg.Is<SaveSettingsCommand>(c => c.Settings.MaxAnswerTokens == 1500),
            Arg.Any<CancellationToken>());
    }
}
