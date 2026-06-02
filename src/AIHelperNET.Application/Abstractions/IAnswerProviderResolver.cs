namespace AIHelperNET.Application.Abstractions;

/// <summary>Resolves the active <see cref="IAnswerProvider"/> for the given backend.</summary>
public interface IAnswerProviderResolver
{
    /// <summary>Returns the provider for <paramref name="backend"/>.</summary>
    IAnswerProvider Resolve(AiBackend backend);
}
