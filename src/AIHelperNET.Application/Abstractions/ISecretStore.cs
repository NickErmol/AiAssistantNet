using System.Security;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for securely storing and retrieving the AI API key.</summary>
public interface ISecretStore
{
    /// <summary>Persists the API key.</summary>
    /// <param name="key">The key as a secure string.</param>
    Result SaveApiKey(SecureString key);

    /// <summary>Retrieves the stored API key.</summary>
    Result<SecureString> GetApiKey();

    /// <summary>Removes the stored API key.</summary>
    Result DeleteApiKey();

    /// <summary>Returns true if an API key is currently stored.</summary>
    bool HasApiKey();
}
