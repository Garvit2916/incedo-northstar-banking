using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// Enables `dotnet ef` tooling to construct the context without booting the API.
/// Provider and connection string are read from configuration/environment so a
/// migration can be produced for either SQLite (development) or SQL Server
/// (production) without editing code.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "Sqlite";
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                         ?? (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase)
                             ? "Server=(localdb)\\MSSQLLocalDB;Database=Northstar;Trusted_Connection=True;TrustServerCertificate=True"
                             : "Data Source=northstar.db");

        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            builder.UseSqlServer(connection);
        }
        else
        {
            builder.UseSqlite(connection);
        }

        return new AppDbContext(builder.Options, TimeProvider.System);
    }
}
