using AIHelperNET.Domain.Questions;
using AIHelperNET.Integration.Tests.Eval;
using FluentAssertions;
using Xunit;

namespace AIHelperNET.Integration.Tests.Eval;

public class ConfusionMatrixTests
{
    [Fact]
    public void Accuracy_CountsCorrectOverTotal()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion);   // correct
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued); // wrong
        m.Record(BoundaryLabel.Unrelated, BoundaryLabel.Unrelated);       // correct

        m.Total.Should().Be(3);
        m.Accuracy.Should().BeApproximately(2.0 / 3.0, 1e-9);
    }

    [Fact]
    public void EmptyMatrix_AccuracyIsZero()
    {
        new ConfusionMatrix().Accuracy.Should().Be(0.0);
    }

    [Fact]
    public void Count_ReturnsCellTally()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued);
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued);

        m.Count(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued).Should().Be(2);
        m.Count(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion).Should().Be(0);
    }

    [Fact]
    public void PrecisionAndRecall_ComputedPerLabel()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.NewQuestion);       // TP
        m.Record(BoundaryLabel.NewQuestion, BoundaryLabel.QuestionContinued); // FN for NewQuestion
        m.Record(BoundaryLabel.Unrelated,   BoundaryLabel.NewQuestion);       // FP for NewQuestion

        m.PrecisionFor(BoundaryLabel.NewQuestion).Should().BeApproximately(0.5, 1e-9);
        m.RecallFor(BoundaryLabel.NewQuestion).Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void PrecisionRecall_ZeroWhenLabelAbsent()
    {
        var m = new ConfusionMatrix();
        m.Record(BoundaryLabel.Unrelated, BoundaryLabel.Unrelated);

        m.PrecisionFor(BoundaryLabel.NewQuestion).Should().Be(0.0);
        m.RecallFor(BoundaryLabel.NewQuestion).Should().Be(0.0);
    }
}
