namespace AIHelperNET.Application.Reviews;

/// <summary>The markdown report and the model that generated it, returned by <see cref="Abstractions.ISessionReviewAnalyzer"/>.</summary>
/// <param name="Markdown">The generated review markdown.</param>
/// <param name="ModelUsed">The concrete model identifier used (e.g. "claude-sonnet-4-6").</param>
public sealed record SessionReviewResult(string Markdown, string ModelUsed);
