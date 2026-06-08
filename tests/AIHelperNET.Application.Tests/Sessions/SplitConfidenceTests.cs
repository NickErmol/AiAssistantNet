using AIHelperNET.Application.Sessions;
using AIHelperNET.Domain.Questions;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Application.Tests.Sessions;

public class SplitConfidenceTests
{
    [Fact]
    public void NoOtherOpinion_KeepsConfidence_AndAgrees()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.85, otherLabel: null);

        effective.Should().Be(0.85);
        agreed.Should().BeTrue();
    }

    [Fact]
    public void BothSayNewQuestion_NoDemotion()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.85, otherLabel: BoundaryLabel.NewQuestion);

        effective.Should().Be(0.85);
        agreed.Should().BeTrue();
    }

    [Theory]
    [InlineData(BoundaryLabel.QuestionContinued)]
    [InlineData(BoundaryLabel.ClarificationOfCurrentQuestion)]
    [InlineData(BoundaryLabel.AdditionalRequirement)]
    public void FinalNewQuestion_OtherIsContinuationFamily_Demotes(BoundaryLabel other)
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.NewQuestion, 0.90, otherLabel: other);

        effective.Should().Be(0.90 * SplitConfidence.DisagreementPenalty);
        agreed.Should().BeFalse();
    }

    [Fact]
    public void FinalContinuation_OtherIsNewQuestion_AlsoDemotes_Symmetric()
    {
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.QuestionContinued, 0.90, otherLabel: BoundaryLabel.NewQuestion);

        effective.Should().Be(0.90 * SplitConfidence.DisagreementPenalty);
        agreed.Should().BeFalse();
    }

    [Fact]
    public void UnrelatedDisagreement_NotAboutSplitting_NoDemotion()
    {
        // Neither side is NewQuestion → not a split disagreement → no demotion.
        var (effective, agreed) = SplitConfidence.Resolve(
            BoundaryLabel.QuestionComplete, 0.80, otherLabel: BoundaryLabel.QuestionContinued);

        effective.Should().Be(0.80);
        agreed.Should().BeTrue();
    }
}
