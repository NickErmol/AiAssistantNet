using AIHelperNET.Infrastructure.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AIHelperNET.Infrastructure.Persistence;

/// <summary>
/// Design-time factory so <c>dotnet ef</c> can instantiate <see cref="AppDbContext"/> without
/// running the WPF App host (which builds its DI host inside <c>OnStartup</c> and is therefore
/// not discoverable by EF tooling). Used only by migration scaffolding — never at runtime.
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <inheritdoc/>
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={AppPaths.DatabaseFile}")
            .Options;
        return new AppDbContext(options);
    }
}
