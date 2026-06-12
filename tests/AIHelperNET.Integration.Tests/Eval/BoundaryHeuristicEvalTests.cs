using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

public class BoundaryHeuristicEvalTests(ITestOutputHelper output)
{
    // measured 2026-06-11 over the expanded 55-entry corpus: 0.600 (floored to the 0.05 step
    // below). The corpus now intentionally contains ~22 over-split continuations / additional
    // requirements / Me-clarifications beyond the simple heuristic's reach — the AI classifier
    // (Spec 3d) is what catches those, so ~33/55 is the healthy heuristic-correct count. This
    // floor only guards against a CATASTROPHIC heuristic regression, not a moderate one; if you
    // tighten the heuristic, re-measure and raise it toward the new measured value.
    private const double Baseline = 0.60;

    [Fact]
    public void Heuristic_MeetsAccuracyBaseline_OverCorpus()
    {
        var detector = new QuestionBoundaryDetector();
        var corpus = CorpusLoader.Load();
        var matrix = new ConfusionMatrix();

        foreach (var entry in corpus)
        {
            // recentQuestions approximates the pipeline's duplicate-detection input (prior
            // detected-question texts); here we use the corpus's Other-speaker recent texts.
            var recentQuestions = entry.RecentItems
                .Where(i => i.Speaker == Speaker.Other)
                .Select(i => i.Text)
                .ToList();

            var result = detector.Evaluate(
                entry.LatestItem.Text, entry.LatestItem.Speaker, entry.ActiveTurnStatus, recentQuestions);

            matrix.Record(entry.ExpectedLabel, result.Classification);
        }

        output.WriteLine(EvalReport.ToText(matrix, "Heuristic (QuestionBoundaryDetector)"));
        matrix.Accuracy.Should().BeGreaterThanOrEqualTo(Baseline);
    }
}
