using System.Net;
using System.Security;
using AdysTech.CredentialManager;
using AIHelperNET.Application.Abstractions;
using FluentResults;

namespace AIHelperNET.Infrastructure.Security;

public sealed class WindowsCredentialSecretStore : ISecretStore
{
    private const string Target = "AIHelperNET:ClaudeApiKey";

    public Result SaveApiKey(SecureString key)
    {
        try
        {
            CredentialManager.SaveCredentials(Target,
                new NetworkCredential(string.Empty, key));
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not save API key").CausedBy(ex));
        }
    }

    public Result<SecureString> GetApiKey()
    {
        var cred = CredentialManager.GetCredentials(Target);
        return cred is null
            ? Result.Fail<SecureString>("No API key stored.")
            : Result.Ok(cred.SecurePassword);
    }

    public bool HasApiKey() => CredentialManager.GetCredentials(Target) is not null;

    public Result DeleteApiKey()
    {
        try
        {
            CredentialManager.RemoveCredentials(Target);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return Result.Fail(new Error("Could not delete API key").CausedBy(ex));
        }
    }
}
