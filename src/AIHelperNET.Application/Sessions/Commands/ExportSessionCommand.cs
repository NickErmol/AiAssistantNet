using AIHelperNET.Application.Abstractions;
using AIHelperNET.Domain.Ids;
using FluentResults;
using Mediator;

namespace AIHelperNET.Application.Sessions.Commands;

/// <summary>Output format for session export.</summary>
public enum ExportFormat
{
    /// <summary>Plain text format.</summary>
    Txt,

    /// <summary>Markdown format.</summary>
    Markdown,
}

/// <summary>Exports a session's transcript and answers to a file.</summary>
/// <param name="SessionId">The session to export.</param>
/// <param name="Format">Target file format.</param>
/// <param name="OutputPath">Absolute path for the output file.</param>
public sealed record ExportSessionCommand(
    SessionId SessionId,
    ExportFormat Format,
    string OutputPath) : IRequest<Result>;

/// <summary>Handles <see cref="ExportSessionCommand"/>.</summary>
public sealed class ExportSessionHandler(
    ISessionRepository repository,
    IExportService exportService) : IRequestHandler<ExportSessionCommand, Result>
{
    /// <inheritdoc/>
    public async ValueTask<Result> Handle(ExportSessionCommand request, CancellationToken cancellationToken)
    {
        var detail = await repository.GetDetailAsync(request.SessionId, cancellationToken);
        if (detail is null) return Result.Fail("Session not found.");

        var content = request.Format == ExportFormat.Markdown
            ? exportService.ToMarkdown(detail)
            : exportService.ToTxt(detail);

        await File.WriteAllTextAsync(request.OutputPath, content, cancellationToken);
        return Result.Ok();
    }
}
