using System.Security;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Commands;
using AIHelperNET.Application.Sessions.Queries;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class ApiKeyCommandKindTests
{
    [Fact]
    public async Task SaveApiKeyHandler_PassesKindThrough()
    {
        var store = Substitute.For<ISecretStore>();
        store.SaveApiKey(SecretKind.Deepgram, Arg.Any<SecureString>()).Returns(Result.Ok());
        var key = new SecureString(); key.AppendChar('k'); key.MakeReadOnly();

        await new SaveApiKeyHandler(store).Handle(
            new SaveApiKeyCommand(SecretKind.Deepgram, key), CancellationToken.None);

        store.Received(1).SaveApiKey(SecretKind.Deepgram, Arg.Any<SecureString>());
    }

    [Fact]
    public async Task HasApiKeyHandler_PassesKindThrough()
    {
        var store = Substitute.For<ISecretStore>();
        store.HasApiKey(SecretKind.Deepgram).Returns(true);

        var result = await new HasApiKeyHandler(store).Handle(
            new HasApiKeyQuery(SecretKind.Deepgram), CancellationToken.None);

        Assert.True(result.Value);
        store.Received(1).HasApiKey(SecretKind.Deepgram);
    }
}
