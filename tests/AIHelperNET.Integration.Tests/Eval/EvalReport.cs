using System.Globalization;
using System.Text;
using AIHelperNET.Domain.Questions;

namespace AIHelperNET.Integration.Tests.Eval;

/// <summary>Renders a <see cref="ConfusionMatrix"/> to a human-readable text block.</summary>
public static class EvalReport
{
    private static readonly BoundaryLabel[] Labels = Enum.GetValues<BoundaryLabel>();

    /// <summary>Builds a report: overall accuracy, a per-label precision/recall table, and the confusion grid.</summary>
    public static string ToText(ConfusionMatrix matrix, string title)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"=== {title} ===");
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Total={matrix.Total} Accuracy={matrix.Accuracy:P1}");
        sb.AppendLine();

        sb.AppendLine("Per-label precision / recall:");
        foreach (var label in Labels)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  {label,-32} P={matrix.PrecisionFor(label):P0}  R={matrix.RecallFor(label):P0}");
        sb.AppendLine();

        sb.AppendLine("Confusion (rows=expected, cols=predicted), non-zero cells only:");
        foreach (var exp in Labels)
            foreach (var pred in Labels)
            {
                var n = matrix.Count(exp, pred);
                if (n > 0)
                    sb.AppendLine(CultureInfo.InvariantCulture,
                        $"  {exp} -> {pred}: {n}{(exp == pred ? "" : "   <-- miss")}");
            }
        return sb.ToString();
    }
}
