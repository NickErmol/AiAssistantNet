using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Settings;

public sealed class SaveSettingsCommandTests
{
    [Fact]
    public async Task Handle_CallsSaveAsync_WithProvidedSettings()
    {
        var store = Substitute.For<ISettingsStore>();
        var handler = new SaveSettingsHandler(store);
        var settings = new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Base,
            AnswerSettings.Default, CodeProfile.Empty,
            null, null);

        var result = await handler.Handle(new SaveSettingsCommand(settings), CancellationToken.None);

        Assert.True(result.IsSuccess);
        await store.Received(1).SaveAsync(settings, Arg.Any<CancellationToken>());
    }
}
