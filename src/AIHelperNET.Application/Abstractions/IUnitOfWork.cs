using FluentResults;

namespace AIHelperNET.Application.Abstractions;

/// <summary>Port for committing pending changes to the data store.</summary>
public interface IUnitOfWork
{
    /// <summary>Saves all pending changes.</summary>
    /// <param name="ct">Cancellation token.</param>
    Task<Result> SaveChangesAsync(CancellationToken ct);
}
