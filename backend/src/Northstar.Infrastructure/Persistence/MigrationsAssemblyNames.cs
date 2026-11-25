namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// Migration assemblies are provider-specific. SQLite migrations remain in the
/// Infrastructure assembly; PostgreSQL migrations live in their own assembly so
/// EF Core only sees the migration set for the selected provider.
/// </summary>
internal static class MigrationsAssemblyNames
{
    public const string Postgres = "Northstar.Infrastructure.Persistence.PostgresMigrations";
}
