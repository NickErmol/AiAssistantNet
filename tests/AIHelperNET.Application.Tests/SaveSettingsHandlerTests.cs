using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Dtos;
using AIHelperNET.Domain.ValueObjects;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests;

public class SaveSettingsHandlerTests
{
    [Fact]
    public async Task Handle_ClampsMaxAnswerTokens_BeforeSaving()
    {
        var store = Substitute.For<ISettingsStore>();
        var handler = new SaveSettingsHandler(store);
        var dto = new AppSettingsDto(
            AiBackend.Claude, WhisperModelSize.Medium, AnswerSettings.Default, CodeProfile.Empty, null, null)
            with { MaxAnswerTokens = 9000 };

        await handler.Handle(new SaveSettingsCommand(dto), CancellationToken.None);

        await store.Received(1).SaveAsync(
            Arg.Is<AppSettingsDto>(d => d.MaxAnswerTokens == 4000), Arg.Any<CancellationToken>());
    }
}
