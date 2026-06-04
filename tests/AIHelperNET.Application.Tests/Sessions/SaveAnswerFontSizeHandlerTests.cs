using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SaveAnswerFontSizeHandlerTests
{
    private static AppSettingsDto MakeSettings(int fontSize = 12) => new(
        AiBackend.Claude,
        WhisperModelSize.Base,
        AnswerSettings.Default,
        CodeProfile.Empty,
        null,
        null,
        fontSize);

    [Fact]
    public async Task Handle_ValueInRange_SavesExactValue()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        var result = await handler.Handle(new SaveAnswerFontSizeCommand(15), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == 15),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValueBelowMin_ClampsToMin()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(1), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == SaveAnswerFontSizeHandler.Min),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValueAboveMax_ClampsToMax()
    {
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(MakeSettings(12));

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(99), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s => s.AnswerFontSize == SaveAnswerFontSizeHandler.Max),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PreservesOtherSettingsFields()
    {
        var original = MakeSettings(12);
        var store = Substitute.For<ISettingsStore>();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(original);

        var handler = new SaveAnswerFontSizeHandler(store);
        await handler.Handle(new SaveAnswerFontSizeCommand(16), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(s =>
                s.ActiveBackend   == original.ActiveBackend   &&
                s.WhisperModel    == original.WhisperModel    &&
                s.AnswerSettings  == original.AnswerSettings  &&
                s.CodeProfile     == original.CodeProfile     &&
                s.MicDeviceId     == original.MicDeviceId     &&
                s.LoopbackDeviceId == original.LoopbackDeviceId &&
                s.AnswerFontSize  == 16),
            Arg.Any<CancellationToken>());
    }
}
