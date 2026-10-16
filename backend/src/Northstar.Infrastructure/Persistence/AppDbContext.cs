using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Northstar.Application.Abstractions;
using Northstar.Domain.Common;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;

namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// The single write model for the platform. In addition to the Identity tables it
/// exposes the domain aggregates required by the onboarding and support
/// workflows. Timestamps and optimistic-concurrency tokens are maintained here so
/// application services cannot forget them.
/// </summary>
public class AppDbContext :
    IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>,
    IApplicationDbContext
{
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider timeProvider)
        : base(options)
    {
        _timeProvider = timeProvider;
    }

    public DbSet<CustomerProfile> CustomerProfiles => Set<CustomerProfile>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Domain.Entities.Application> Applications => Set<Domain.Entities.Application>();
    public DbSet<ApplicationStatusHistory> ApplicationStatusHistory => Set<ApplicationStatusHistory>();
    public DbSet<KycDocument> KycDocuments => Set<KycDocument>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<TicketAssignment> TicketAssignments => Set<TicketAssignment>();
    public DbSet<AiAssistanceRecord> AiAssistanceRecords => Set<AiAssistanceRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Store all timestamps as UTC ticks so ordering and comparisons translate
        // on SQLite (which cannot order DateTimeOffset columns) as well as SQL Server.
        configurationBuilder.Properties<DateTimeOffset>()
            .HaveConversion<DateTimeOffsetToUtcTicksConverter>();
        configurationBuilder.Properties<DateTimeOffset?>()
            .HaveConversion<NullableDateTimeOffsetToUtcTicksConverter>();

        base.ConfigureConventions(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    async Task<IDbContextTransaction> IApplicationDbContext.BeginTransactionAsync(CancellationToken ct) =>
        await Database.BeginTransactionAsync(ct);

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        PromoteNewChildren();
        ApplyAuditAndConcurrency();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Child rows that are created by the domain and appended to a tracked parent's
    /// collection rather than added through their own DbSet. Because they carry a
    /// client-generated GUID key, EF Core assumes a populated key means an existing
    /// row and attaches them as Modified, so an INSERT becomes a no-op UPDATE that
    /// trips the concurrency token.
    ///
    /// When EF Core attaches a graph-discovered entity as Modified it copies the
    /// current values into the original values, so every property reports as
    /// modified even though nothing actually changed. A row that was loaded from the
    /// database and then genuinely edited - for example an assignment whose
    /// <c>UnassignedAtUtc</c> is stamped on reassignment - has original values that
    /// differ from the current values. Promoting only entries with no real value
    /// change therefore inserts new children while a loaded, edited row correctly
    /// remains an UPDATE.
    /// </summary>
    private static readonly HashSet<Type> AppendOnlyChildTypes = new()
    {
        typeof(ApplicationStatusHistory),
        typeof(TicketMessage),
        typeof(TicketAssignment)
    };

    private void PromoteNewChildren()
    {
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            var isCandidate = entry.State == EntityState.Modified
                && AppendOnlyChildTypes.Contains(entry.Entity.GetType());

            if (!isCandidate)
            {
                continue;
            }

            var hasRealChange = entry.Properties.Any(
                property => !Equals(property.OriginalValue, property.CurrentValue));

            if (!hasRealChange)
            {
                entry.State = EntityState.Added;
            }
        }
    }

    private void ApplyAuditAndConcurrency()
    {
        var now = _timeProvider.GetUtcNow();

        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    if (entry.Entity.CreatedAtUtc == default)
                    {
                        entry.Entity.CreatedAtUtc = now;
                    }

                    entry.Entity.UpdatedAtUtc = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAtUtc = now;
                    entry.Entity.Version++;
                    break;
            }
        }
    }
}
