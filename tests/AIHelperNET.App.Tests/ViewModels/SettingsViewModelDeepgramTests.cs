using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using FluentResults;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests.ViewModels;

/// <summary>Verifies the Audio tab's Deepgram key entry commands target
/// <see cref="SecretKind.Deepgram"/> and that selecting the Deepgram provider toggles
/// the key panel's visibility flag.</summary>
public class SettingsViewModelDeepgramTests
{
    // Mirrors the ctor-argument style of the existing SettingsViewModel tests in this project
    // (see SettingsViewModelTokenTests.cs): substitute IMediator + the other ctor dependencies.
    private static SettingsViewModel MakeSut(IMediator mediator) => new(
        mediator,
        new StubHotkeyApplier(),
        GlossaryStubs.Empty(),
        Substitute.For<IDocumentTextExtractor>(),
        Substitute.For<IProfileCondenser>());

    [Fact]
    public async Task SaveDeepgramKey_SendsCommandWithDeepgramKind_AndClearsInput()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<SaveApiKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        var sut = MakeSut(mediator);
        sut.DeepgramKeyInput = "dg-secret";

        await sut.SaveDeepgramKeyCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<SaveApiKeyCommand>(c => c.Kind == SecretKind.Deepgram), Arg.Any<CancellationToken>());
        Assert.Equal(string.Empty, sut.DeepgramKeyInput);
    }

    [Fact]
    public async Task DeleteDeepgramKey_SendsCommandWithDeepgramKind()
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<DeleteApiKeyCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        var sut = MakeSut(mediator);

        await sut.DeleteDeepgramKeyCommand.ExecuteAsync(null);

        await mediator.Received(1).Send(
            Arg.Is<DeleteApiKeyCommand>(c => c.Kind == SecretKind.Deepgram), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void SelectingDeepgram_TogglesKeyPanelVisibility()
    {
        var sut = MakeSut(Substitute.For<IMediator>());
        Assert.False(sut.IsDeepgramSelected);
        sut.SttProvider = SttProvider.Deepgram;
        Assert.True(sut.IsDeepgramSelected);
    }
}
