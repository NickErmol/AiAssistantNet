using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIHelperNET.Application.Abstractions;
using Serilog;

namespace AIHelperNET.Infrastructure.Diagnostics;

/// <summary>
/// Best-effort <see cref="IBoundaryDecisionRecorder"/> that appends one JSON line per decision to a
/// dated file under the diagnostics directory. All I/O failures are swallowed (logged at Debug) so
/// recording never disturbs the transcript pipeline. Appends are serialized by a lock to keep lines
/// intact across threads.
/// </summary>
/// <param name="directory">The directory to write the dated JSONL files into.</param>
public sealed class JsonlBoundaryDecisionRecorder(string directory) : IBoundaryDecisionRecorder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _gate = new();

    /// <inheritdoc/>
    public void Record(BoundaryDecisionRecord record)
    {
        try
        {
            var file = Path.Combine(
                directory, $"boundary-decisions-{record.Timestamp:yyyy-MM-dd}.jsonl");
            var line = JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine;

            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(file, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "JsonlBoundaryDecisionRecorder: failed to record decision");
        }
    }
}
