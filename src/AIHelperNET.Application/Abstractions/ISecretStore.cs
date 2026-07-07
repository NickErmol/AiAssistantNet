using System.Security;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Names a stored secret; each kind maps to its own credential-manager entry.</summary>
public enum SecretKind
{
    /// <summary>Anthropic Claude API key.</summary>
    Anthropic,
    /// <summary>Deepgram streaming STT API key.</summary>
    Deepgram
}

/// <summary>Port for storing API keys in the OS secret store.</summary>
public interface ISecretStore
{
    /// <summary>Persists the API key for <paramref name="kind"/>.</summary>
    Result SaveApiKey(SecretKind kind, SecureString key);

    /// <summary>Retrieves the stored API key for <paramref name="kind"/>.</summary>
    Result<SecureString> GetApiKey(SecretKind kind);

    /// <summary>Removes the stored API key for <paramref name="kind"/>.</summary>
    Result DeleteApiKey(SecretKind kind);

    /// <summary>Returns true if an API key is stored for <paramref name="kind"/>.</summary>
    bool HasApiKey(SecretKind kind);
}
