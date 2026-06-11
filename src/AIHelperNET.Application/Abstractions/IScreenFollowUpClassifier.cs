using AIHelperNET.Application.Answers;

namespace AIHelperNET.Application.Abstractions;

/// <summary>
/// Port for the focused decision made while a captured on-screen task is in focus: does an
/// interviewer utterance add to / ask about that task (<see cref="ScreenFollowUpOutcome.FollowUp"/>),
/// start an unrelated new topic (<see cref="ScreenFollowUpOutcome.MovedOn"/>), or is it filler
/// (<see cref="ScreenFollowUpOutcome.Noise"/>)? Distinct from <see cref="IQuestionBoundaryClassifier"/>,
/// which detects audio turn boundaries and is blind to the captured task.
/// </summary>
public interface IScreenFollowUpClassifier
{
    /// <summary>Classifies an interviewer utterance against the captured screen task.</summary>
    /// <param name="taskSummary">The captured task text (OCR) the interviewer is working from.</param>
    /// <param name="additions">Follow-ups already attached to this task, oldest → newest.</param>
    /// <param name="utterance">The latest interviewer utterance to classify.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The follow-up action to take.</returns>
    Task<ScreenFollowUpOutcome> ClassifyAsync(
        string taskSummary,
        IReadOnlyList<string> additions,
        string utterance,
        CancellationToken ct);
}
