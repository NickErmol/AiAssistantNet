namespace AIHelperNET.Infrastructure.AI;

public sealed class OllamaOptions
{
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string Model   { get; set; } = "qwen2.5-coder:7b";
}
