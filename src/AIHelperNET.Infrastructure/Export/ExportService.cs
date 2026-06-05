using System.Globalization;
using System.Text;
using AIHelperNET.Application.Abstractions;
using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Infrastructure.Export;

public sealed class ExportService : IExportService
{
    public string ToTxt(SessionDetailDto detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"Session: {detail.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm} | Mode: {detail.Mode}");
        sb.AppendLine(new string('-', 60));
        sb.AppendLine();
        sb.AppendLine("TRANSCRIPT");
        foreach (var item in detail.Transcript)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"[{item.Timestamp.ToLocalTime():HH:mm:ss}] {item.Speaker}: {item.Text}");
        sb.AppendLine();
        sb.AppendLine("ANSWERS");
        foreach (var a in detail.Answers)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Q: {a.QuestionText}");
            sb.AppendLine(CultureInfo.InvariantCulture, $"A: {a.AnswerText}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public string ToMarkdown(SessionDetailDto detail)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"## Session — {detail.StartedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Mode:** {detail.Mode}");
        sb.AppendLine();
        sb.AppendLine("### Transcript");
        sb.AppendLine();
        foreach (var item in detail.Transcript)
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"> {item.Speaker}: {item.Text}  ");
        sb.AppendLine();
        sb.AppendLine("### Answers");
        foreach (var a in detail.Answers)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"### Q: {a.QuestionText}");
            sb.AppendLine();
            sb.AppendLine(a.AnswerText);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
