using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class AsrConfidenceGateTests
{
    private readonly AsrConfidenceGate _gate = new();

    [Theory]
    [InlineData(BoundaryLabel.QuestionContinued)]
    [InlineData(BoundaryLabel.AdditionalRequirement)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion)]
    public void Drops_LowConfidenceFoldLabel_WithLiveTurn(BoundaryLabel fold)
    {
        _gate.ShouldDrop(asrConfidence: 0.30, foldLabel: fold, liveTurnExists: true)
            .Should().BeTrue();
    }

    [Fact]
    public void DoesNotDrop_WhenConfidenceAtOrAboveFloor()
    {
        _gate.ShouldDrop(asrConfidence: AsrConfidenceGate.AsrFloor,
            foldLabel: BoundaryLabel.QuestionContinued, liveTurnExists: true)
            .Should().BeFalse();
    }

    [Fact]
    public void DoesNotDrop_WhenNoLiveTurn()
    {
        _gate.ShouldDrop(asrConfidence: 0.10,
            foldLabel: BoundaryLabel.QuestionContinued, liveTurnExists: false)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(BoundaryLabel.NewQuestion)]
    [InlineData(BoundaryLabel.QuestionComplete)]
    [InlineData(BoundaryLabel.TaskComplete)]
    [InlineData(BoundaryLabel.QuestionStarted)]
    [InlineData(BoundaryLabel.Unrelated)]
    [InlineData(BoundaryLabel.NoQuestion)]
    public void DoesNotDrop_NonFoldLabel_EvenWhenLowConfidence(BoundaryLabel label)
    {
        _gate.ShouldDrop(asrConfidence: 0.10, foldLabel: label, liveTurnExists: true)
            .Should().BeFalse();
    }

    [Fact]
    public void IsFoldLabel_RecognizesTheThreeFoldLabels()
    {
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.QuestionContinued).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.AdditionalRequirement).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.ClarificationOfCurrentQuestion).Should().BeTrue();
        AsrConfidenceGate.IsFoldLabel(BoundaryLabel.NewQuestion).Should().BeFalse();
    }
}
