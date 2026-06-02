using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Answers;
using Microsoft.Extensions.Options;
using OllamaSharp;
using OllamaSharp.Models;

namespace AIHelperNET.Infrastructure.AI;

public sealed class OllamaAnswerProvider(
    IOllamaApiClient client,
    IOptions<OllamaOptions> options) : IAnswerProvider
{
    public AiBackend Backend => AiBackend.Ollama;

    public async IAsyncEnumerable<string> StreamAnswerAsync(
        AnswerPrompt prompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var request = new GenerateRequest
        {
            Model  = options.Value.Model,
            Prompt = $"{prompt.System}\n\n{prompt.User}",
            Stream = true
        };

        await foreach (var token in client.GenerateAsync(request, ct))
        {
            if (token?.Response is { Length: > 0 } t)
                yield return t;
        }
    }
}
