using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AIHelperNET.Infrastructure.Persistence;

/// <summary>
/// Applies SQLite performance pragmas on every opened connection. WAL is a database-level
/// property (persists in the file header), but <c>synchronous</c> is per-connection and resets to
/// the default on each open — so both are set here to guarantee they apply to every pooled
/// connection.
/// </summary>
/// <remarks>
/// The per-segment <c>SaveChangesAsync</c> in <c>TranscriptPipelineService</c> runs on the serial
/// consumer thread and gates the next segment. <c>journal_mode=WAL</c> + <c>synchronous=NORMAL</c>
/// turns those writes from a full rollback-journal fsync into a cheap WAL append, shrinking that
/// on-the-hot-path cost. WAL + NORMAL is crash-safe against application crashes; only an OS/power
/// loss can lose the most recent transaction — acceptable for recoverable transcript data.
/// </remarks>
public sealed class SqlitePragmaInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL;";

    /// <inheritdoc/>
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        cmd.ExecuteNonQuery();
    }

    /// <inheritdoc/>
    public override async Task ConnectionOpenedAsync(
        DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = Pragmas;
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }
}
