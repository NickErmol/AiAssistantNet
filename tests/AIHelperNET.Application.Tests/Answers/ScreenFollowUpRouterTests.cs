using AIHelperNET.Application.Answers;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Answers;

public class ScreenFollowUpRouterTests
{
    [Theory]
    [InlineData(BoundaryLabel.AdditionalRequirement,          ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion, ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.QuestionContinued,              ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.QuestionComplete,               ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.TaskComplete,                   ScreenFollowUpOutcome.FollowUp)]
    [InlineData(BoundaryLabel.NewQuestion,                    ScreenFollowUpOutcome.MovedOn)]
    [InlineData(BoundaryLabel.NoQuestion,                     ScreenFollowUpOutcome.Noise)]
    [InlineData(BoundaryLabel.Unrelated,                      ScreenFollowUpOutcome.Noise)]
    [InlineData(BoundaryLabel.QuestionStarted,                ScreenFollowUpOutcome.Noise)]
    public void Map_MapsLabelToOutcome(BoundaryLabel label, ScreenFollowUpOutcome expected)
        => ScreenFollowUpRouter.Map(label).Should().Be(expected);
}
