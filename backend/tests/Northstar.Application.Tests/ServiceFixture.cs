using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Services;
using Northstar.Application.Validation;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Application.Tests;

/// <summary>
/// Hosts one in-memory SQLite database for the lifetime of a test and wires the
/// application services against controllable test doubles. Services therefore
/// run against the real EF Core stack - including the append-only child
/// promotion logic in <see cref="AppDbContext"/> - without touching the file
/// database used by the API.
/// </summary>
internal sealed class ServiceFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public ServiceFixture()
    {
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options, TimeProvider.System);
        Db.Database.EnsureCreated();
    }

    public AppDbContext Db { get; }

    public TestClock Clock { get; } = new();

    public TestCurrentUser Caller { get; } = new();

    public TestAuditWriter Audit { get; } = new();

    public TestReferenceGenerator References { get; } = new();

    public TestUserDirectory Users { get; } = new();

    public NotificationService Notifications => new(Db, Caller, Clock);

    public ApplicationService Applications =>
        new(
            Db,
            Caller,
            Clock,
            Audit,
            References,
            Notifications,
            new CreateApplicationRequestValidator(),
            new UpdateApplicationDraftRequestValidator(),
            new ReasonRequestValidator());

    public TicketService Tickets =>
        new(
            Db,
            Caller,
            Clock,
            Audit,
            References,
            Users,
            new CreateTicketRequestValidator(),
            new AddTicketMessageRequestValidator(),
            new AssignTicketRequestValidator());

    /// <summary>
    /// Seeds an Identity user. Customer profiles and notifications hold foreign
    /// keys to AspNetUsers, so any user id referenced by persisted rows must exist.
    /// </summary>
    public ApplicationUser SeedUser(string? email = null, bool isActive = true)
    {
        var address = email ?? $"user-{Guid.NewGuid():N}@northstar.example";
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = address,
            Email = address,
            FullName = "Test User",
            IsActive = isActive,
            CreatedAtUtc = Clock.UtcNow
        };

        Db.Users.Add(user);
        Db.SaveChanges();
        return user;
    }

    /// <summary>
    /// Seeds a customer profile owned by a new (or supplied) Identity user. An
    /// incomplete profile fails <c>IsComplete</c>, which is what submission checks.
    /// </summary>
    public CustomerProfile SeedProfile(
        Guid? userId = null,
        bool complete = true,
        DateOnly? dateOfBirth = null)
    {
        var ownerId = userId ?? SeedUser().Id;

        var profile = new CustomerProfile
        {
            UserId = ownerId,
            FirstName = "Ada",
            LastName = "Lovelace",
            PhoneNumber = complete ? "+44 7700 900000" : null,
            DateOfBirth = dateOfBirth ?? new DateOnly(1990, 5, 4),
            AddressLine1 = complete ? "12 Example Street" : null,
            City = complete ? "London" : null,
            PostalCode = complete ? "EC1A 1BB" : null,
            Country = complete ? "United Kingdom" : null
        };

        Db.CustomerProfiles.Add(profile);
        Db.SaveChanges();
        return profile;
    }

    /// <summary>Seeds an active (or deliberately inactive) demonstration product.</summary>
    public Product SeedProduct(int minimumAge = 18, bool isActive = true)
    {
        var product = new Product
        {
            Code = $"PRD-{Guid.NewGuid():N}"[..15],
            Name = "Everyday Savings Account",
            Summary = "Illustrative demonstration product.",
            Description = "Demonstration copy only; not a real financial offer.",
            Category = "Savings",
            Eligibility = "UK residents aged 18 and over.",
            RequiredDocuments = new List<string> { "ProofOfIdentity", "ProofOfAddress" },
            Terms = "Illustrative terms for the demonstration.",
            MinimumAge = minimumAge,
            IsActive = isActive,
            DisplayOrder = 1
        };

        Db.Products.Add(product);
        Db.SaveChanges();
        return product;
    }

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }
}
