using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Condenses a candidate's resume (and optional job description) into a compact
/// briefing card suitable for injecting into live-interview answer prompts.
/// </summary>
public interface IProfileCondenser
{
    /// <summary>
    /// Calls the Claude API (non-streaming, Sonnet) to produce a condensed profile card.
    /// </summary>
    /// <param name="resumeText">The candidate's resume as plain text.</param>
    /// <param name="jobDescriptionText">The target job description as plain text, or <c>null</c> to omit the TARGET ROLE block.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The condensed profile card text on success; a failure result on API or key errors.</returns>
    Task<Result<string>> CondenseAsync(
        string resumeText,
        string? jobDescriptionText,
        CancellationToken ct);
}
