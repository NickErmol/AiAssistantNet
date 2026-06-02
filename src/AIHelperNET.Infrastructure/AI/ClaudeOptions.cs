namespace AIHelperNET.Infrastructure.AI;

public sealed class ClaudeOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com";
    public string Model   { get; set; } = "claude-sonnet-4-6";
    public string Version { get; set; } = "2023-06-01";
}
