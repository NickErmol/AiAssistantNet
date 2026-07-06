using AIHelperNET.Application.Answers;
using AIHelperNET.Application.Reviews;
using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Port that submits an <see cref="AnswerPrompt"/> to the LLM backend and returns the
/// generated review report as a <see cref="SessionReviewResult"/>.
/// </summary>
public interface ISessionReviewAnalyzer
{
    /// <summary>
    /// Sends the prompt to the LLM and returns the markdown report together with the model
    /// identifier that was used.
    /// </summary>
    /// <param name="prompt">The fully-assembled review prompt.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<Result<SessionReviewResult>> AnalyzeAsync(AnswerPrompt prompt, CancellationToken ct);
}
