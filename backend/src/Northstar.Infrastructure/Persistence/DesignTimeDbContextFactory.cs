using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// Enables `dotnet ef` tooling to construct the context without booting the API.
/// The provider and connection string are read from configuration/environment so
/// migrations can be produced for SQLite, PostgreSQL or SQL Server without editing
/// code. Each provider has its own migration assembly.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("Database__Provider") ?? "Sqlite";
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Default");

        var builder = new DbContextOptionsBuilder<AppDbContext>();

        switch (provider.Trim().ToLowerInvariant())
        {
            case "sqlite":
                builder.UseSqlite(
                    connection ?? "Data Source=northstar.db",
                    sqlite => sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
                break;

            case "postgres":
            case "postgresql":
                if (string.IsNullOrWhiteSpace(connection))
                {
                    throw new InvalidOperationException(
                        "Set ConnectionStrings__Default before running EF tooling for PostgreSQL.");
                }

                builder.UseNpgsql(
                    connection,
                    postgres => postgres.MigrationsAssembly(MigrationsAssemblyNames.Postgres));
                break;

            case "sqlserver":
                builder.UseSqlServer(
                    connection ?? "Server=(localdb)\\MSSQLLocalDB;Database=Northstar;Trusted_Connection=True;TrustServerCertificate=True",
                    sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
                break;

            default:
                throw new InvalidOperationException(
                    "Database:Provider must be 'Sqlite', 'Postgres' or 'SqlServer'.");
        }

        return new AppDbContext(builder.Options, TimeProvider.System);
    }
}
