using AIHelperNET.Application.Sessions.Dtos;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Converts session detail into exportable text formats.</summary>
public interface IExportService
{
    /// <summary>Renders the session as plain text.</summary>
    string ToTxt(SessionDetailDto detail);

    /// <summary>Renders the session as Markdown.</summary>
    string ToMarkdown(SessionDetailDto detail);
}
