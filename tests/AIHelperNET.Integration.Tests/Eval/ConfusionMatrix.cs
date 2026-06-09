using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Pure expected×predicted tally for boundary-label classification, with accuracy/precision/recall.</summary>
public sealed class ConfusionMatrix
{
    private readonly Dictionary<(BoundaryLabel Expected, BoundaryLabel Predicted), int> _cells = new();
    private int _total;
    private int _correct;

    /// <summary>Records one (expected, predicted) outcome.</summary>
    public void Record(BoundaryLabel expected, BoundaryLabel predicted)
    {
        var key = (expected, predicted);
        _cells[key] = _cells.TryGetValue(key, out var n) ? n + 1 : 1;
        _total++;
        if (expected == predicted) _correct++;
    }

    /// <summary>Total recorded outcomes.</summary>
    public int Total => _total;

    /// <summary>Fraction correct, or 0 when nothing recorded.</summary>
    public double Accuracy => _total == 0 ? 0.0 : (double)_correct / _total;

    /// <summary>The tally for one cell.</summary>
    public int Count(BoundaryLabel expected, BoundaryLabel predicted) =>
        _cells.TryGetValue((expected, predicted), out var n) ? n : 0;

    /// <summary>Precision for a label: correct predictions of it / all predictions of it (0 if none).</summary>
    public double PrecisionFor(BoundaryLabel label)
    {
        var predicted = _cells.Where(kv => kv.Key.Predicted == label).Sum(kv => kv.Value);
        return predicted == 0 ? 0.0 : (double)Count(label, label) / predicted;
    }

    /// <summary>Recall for a label: correct predictions of it / all actual occurrences of it (0 if none).</summary>
    public double RecallFor(BoundaryLabel label)
    {
        var expected = _cells.Where(kv => kv.Key.Expected == label).Sum(kv => kv.Value);
        return expected == 0 ? 0.0 : (double)Count(label, label) / expected;
    }
}
