using Microsoft.EntityFrameworkCore;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration;

/// <summary>
/// Target context used only by the migration utility. It bypasses the normal
/// SaveChanges hook so original CreatedAtUtc/UpdatedAtUtc/Version values are
/// copied verbatim instead of being stamped with migration-time values.
/// </summary>
public sealed class MigrationAppDbContext : AppDbContext
{
    public MigrationAppDbContext(DbContextOptions<AppDbContext> options)
        : base(options, TimeProvider.System)
    {
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        base.SaveChangesAsync(true, cancellationToken);
}
