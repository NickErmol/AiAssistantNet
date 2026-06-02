using AIHelperNET.Application.Abstractions;

namespace AIHelperNET.Infrastructure.AI;

public sealed class AnswerProviderResolver(
    ClaudeAnswerProvider claude,
    OllamaAnswerProvider ollama) : IAnswerProviderResolver
{
    public IAnswerProvider Resolve(AiBackend backend) => backend switch
    {
        AiBackend.Claude => claude,
        AiBackend.Ollama => ollama,
        _ => throw new ArgumentOutOfRangeException(nameof(backend))
    };
}
