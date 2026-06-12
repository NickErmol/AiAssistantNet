using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Application.Answers;

/// <summary>The action to take for interviewer speech while a captured screen task is in focus.</summary>
public enum ScreenFollowUpOutcome
{
    /// <summary>Speech adds to or asks about the captured task — spawn a new context-aware card.</summary>
    FollowUp,
    /// <summary>Interviewer started a new, unrelated question — drop the screen-task linkage.</summary>
    MovedOn,
    /// <summary>Noise / no question — ignore.</summary>
    Noise
}

/// <summary>Maps an AI boundary label to a <see cref="ScreenFollowUpOutcome"/>. Biased toward keeping
/// the captured task in context: only an explicit new-topic label ends the linkage.</summary>
public static class ScreenFollowUpRouter
{
    /// <summary>Maps <paramref name="label"/> to the screen follow-up action.</summary>
    public static ScreenFollowUpOutcome Map(BoundaryLabel label) => label switch
    {
        BoundaryLabel.AdditionalRequirement
            or BoundaryLabel.ClarificationOfCurrentQuestion
            or BoundaryLabel.QuestionContinued
            or BoundaryLabel.QuestionComplete
            or BoundaryLabel.TaskComplete => ScreenFollowUpOutcome.FollowUp,
        BoundaryLabel.NewQuestion => ScreenFollowUpOutcome.MovedOn,
        _ => ScreenFollowUpOutcome.Noise
    };
}
