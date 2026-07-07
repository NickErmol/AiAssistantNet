using System.Net;
using System.Security;
using AdysTech.CredentialManager;
using AIHelperNET.Application.Abstractions;
using FluentResults;

namespace AIHelperNET.Infrastructure.Security;

public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private static string TargetFor(SecretKind kind) => kind switch
    {
        SecretKind.Anthropic => "AIHelperNET:ClaudeApiKey",   // unchanged — existing stored keys keep working
        SecretKind.Deepgram  => "AIHelperNET:DeepgramApiKey",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public Result SaveApiKey(SecretKind kind, SecureString key)
    {
        try
        {
            CredentialManager.SaveCredentials(TargetFor(kind),
                new NetworkCredential(string.Empty, key));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not save API key").CausedBy(ex));
        }
    }

    public Result<SecureString> GetApiKey(SecretKind kind)
    {
        var cred = CredentialManager.GetCredentials(TargetFor(kind));
        return cred is null
            ? Result.Fail<SecureString>("No API key stored.")
            : Result.Ok(cred.SecurePassword);
    }

    public bool HasApiKey(SecretKind kind) => CredentialManager.GetCredentials(TargetFor(kind)) is not null;

    public Result DeleteApiKey(SecretKind kind)
    {
        try
        {
            CredentialManager.RemoveCredentials(TargetFor(kind));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not delete API key").CausedBy(ex));
        }
    }
}
