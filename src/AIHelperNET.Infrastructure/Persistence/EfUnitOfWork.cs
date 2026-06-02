using AIHelperNET.Application.Abstractions;
using FluentResults;
using Microsoft.EntityFrameworkCore;

namespace AIHelperNET.Infrastructure.Persistence;

public sealed class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return Result.Ok();
        }
        catch (DbUpdateException ex)
        {
            return Result.Fail(new Error("Persistence failed").CausedBy(ex));
        }
    }
}
