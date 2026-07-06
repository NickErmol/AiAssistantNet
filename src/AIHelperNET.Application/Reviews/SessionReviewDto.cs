namespace AIHelperNET.Application.Reviews;

/// <summary>Projection returned by the generate and get review handlers.</summary>
/// <param name="Markdown">The generated markdown report.</param>
/// <param name="ModelUsed">The model identifier used to generate the report.</param>
/// <param name="GeneratedAt">When the review was generated.</param>
public sealed record SessionReviewDto(string Markdown, string ModelUsed, DateTimeOffset GeneratedAt);
