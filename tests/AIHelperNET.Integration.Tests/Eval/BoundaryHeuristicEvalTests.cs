using AIHelperNET.Domain.Questions;
using AIHelperNET.Domain.Sessions;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace AIHelperNET.Integration.Tests.Eval;

public class BoundaryHeuristicEvalTests(ITestOutputHelper output)
{
    // measured 2026-06-09: 0.833  (floored to the 0.05 step below)
    private const double Baseline = 0.80;

    [Fact]
    public void Heuristic_MeetsAccuracyBaseline_OverCorpus()
    {
        var detector = new QuestionBoundaryDetector();
        var corpus = CorpusLoader.Load();
        var matrix = new ConfusionMatrix();

        foreach (var entry in corpus)
        {
            // recentQuestions mirrors the pipeline: prior Other-speaker texts.
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
