using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Northstar.DataMigration;
using Northstar.Domain;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.DataMigration.Tests;

internal sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection($"Data Source=migration-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
        _connection.Open();
    }

    public MigrationAppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        var context = new MigrationAppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
    }
}

internal sealed record SeedData(
    IReadOnlyDictionary<string, Guid> SourceRoleIds,
    Guid Agent2UserId,
    Guid ProductId,
    Guid CustomerProfileId,
    Guid GarvitProfileId,
    Guid ApplicationId,
    Guid TicketId,
    DateTimeOffset FixedTime);

internal static class SeedDataFactory
{
    public static async Task<SeedData> SeedSourceAsync(MigrationAppDbContext context)
    {
        var fixedTime = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var sourceRoleIds = new Dictionary<string, Guid>(StringComparer.Ordinal)
        {
            [Roles.Administrator] = Guid.NewGuid(),
            [Roles.Customer] = Guid.NewGuid(),
            [Roles.SupportAgent] = Guid.NewGuid(),
            [Roles.Reviewer] = Guid.NewGuid()
        };

        foreach (var (name, id) in sourceRoleIds)
        {
            context.Roles.Add(new IdentityRole<Guid>
            {
                Id = id,
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            });
        }

        var admin = User(MigrationScope.AdministratorUserId, "admin@northstar.example", "Alex Admin", fixedTime);
        var customer = User(MigrationScope.CustomerUserId, "customer@northstar.example", "Casey Customer", fixedTime);
        var agent = User(MigrationScope.SupportAgentUserId, "agent@northstar.example", "Sam Agent", fixedTime);
        var reviewer = User(MigrationScope.ReviewerUserId, "reviewer@northstar.example", "Rita Reviewer", fixedTime);
        var garvit = User(MigrationScope.RequiredLinkedUserId, "garvitofficialid@gmail.com", "Garvit Choudhary", fixedTime);
        var optionalUsers = MigrationScope.OptionalLinkedAgentUserIds
            .Select((id, index) => User(
                id,
                $"agent2_{index + 1}@northstar.example",
                "Second Agent",
                fixedTime))
            .ToList();
        var agent2 = optionalUsers.First();
        var unrelated = User(Guid.NewGuid(), "demo@northstar.example", "Demo User", fixedTime);

        context.Users.AddRange(admin, customer, agent, reviewer, garvit, unrelated);
        context.Users.AddRange(optionalUsers);
        var userRoles = new List<IdentityUserRole<Guid>>
        {
            UserRole(admin.Id, sourceRoleIds[Roles.Administrator]),
            UserRole(customer.Id, sourceRoleIds[Roles.Customer]),
            UserRole(agent.Id, sourceRoleIds[Roles.SupportAgent]),
            UserRole(reviewer.Id, sourceRoleIds[Roles.Reviewer]),
            UserRole(garvit.Id, sourceRoleIds[Roles.Customer])
        };
        foreach (var optionalUser in optionalUsers)
        {
            userRoles.Add(UserRole(optionalUser.Id, sourceRoleIds[Roles.Customer]));
            userRoles.Add(UserRole(optionalUser.Id, sourceRoleIds[Roles.SupportAgent]));
        }

        context.UserRoles.AddRange(userRoles);

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Code = "SAV-001",
            Name = "Everyday Savings Account",
            Summary = "summary",
            Description = "description",
            Category = "Savings",
            Eligibility = "eligibility",
            RequiredDocuments = new List<string> { "ProofOfIdentity" },
            Terms = "terms",
            MinimumAge = 18,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var customerProfile = Profile(Guid.NewGuid(), customer.Id, "Casey", "Customer", fixedTime);
        var garvitProfile = Profile(Guid.NewGuid(), garvit.Id, "Garvit", "Choudhary", fixedTime);
        context.Products.Add(product);
        context.CustomerProfiles.AddRange(customerProfile, garvitProfile);

        var application = Domain.Entities.Application.CreateDraft(customerProfile.Id, product.Id, "APP-000001", "{}");
        application.Id = Guid.NewGuid();
        application.Submit(customer.Id, fixedTime);
        application.StartReview(reviewer.Id, fixedTime);
        application.CreatedAtUtc = fixedTime;
        application.UpdatedAtUtc = fixedTime;
        application.Version = 2;

        var garvitApplication = Domain.Entities.Application.CreateDraft(garvitProfile.Id, product.Id, "APP-000002", "{}");
        garvitApplication.Id = Guid.NewGuid();
        garvitApplication.CreatedAtUtc = fixedTime;
        garvitApplication.UpdatedAtUtc = fixedTime;

        var kyc = new KycDocument
        {
            Id = Guid.NewGuid(),
            ApplicationId = application.Id,
            CustomerProfileId = customerProfile.Id,
            DocumentType = KycDocumentType.ProofOfIdentity,
            OriginalFileName = "identity.pdf",
            StorageKey = "kyc/identity.pdf",
            ContentType = "application/pdf",
            SizeBytes = 10,
            Sha256 = new string('A', 64),
            ScanStatus = ScanStatus.SkippedDev,
            ReviewStatus = DocumentReviewStatus.Pending,
            UploadedAtUtc = fixedTime,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var ticket = new Ticket
        {
            Id = Guid.NewGuid(),
            Reference = "TKT-000001",
            Subject = "Help",
            Description = "Description",
            Category = TicketCategory.Other,
            Priority = TicketPriority.Normal,
            CustomerUserId = customer.Id,
            RelatedApplicationId = application.Id,
            AssignedAgentId = agent2.Id,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var assignment = new TicketAssignment
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AgentUserId = agent2.Id,
            AssignedByUserId = admin.Id,
            AssignedAtUtc = fixedTime,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var message = new TicketMessage
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            AuthorUserId = agent2.Id,
            AuthorType = MessageAuthorType.Agent,
            Body = "Message",
            IsInternalNote = false,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var ai = new AiAssistanceRecord
        {
            Id = Guid.NewGuid(),
            TicketId = ticket.Id,
            Kind = AiAssistKind.Summary,
            Provider = "test",
            Model = null,
            IsLiveInference = false,
            InputHash = new string('B', 64),
            OutputJson = "{}",
            Confidence = null,
            LatencyMs = 1,
            CreatedByUserId = agent2.Id,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = garvit.Id,
            ApplicationId = garvitApplication.Id,
            Type = NotificationType.ApplicationSubmitted,
            Title = "Submitted",
            Body = "Body",
            IsRead = false,
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        };

        context.Applications.AddRange(application, garvitApplication);
        context.KycDocuments.Add(kyc);
        context.Tickets.Add(ticket);
        context.TicketAssignments.Add(assignment);
        context.TicketMessages.Add(message);
        context.AiAssistanceRecords.Add(ai);
        context.Notifications.Add(notification);
        context.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = customer.Id,
            TokenHash = new string('C', 128),
            ExpiresAtUtc = fixedTime.AddDays(1),
            CreatedAtUtc = fixedTime,
            UpdatedAtUtc = fixedTime,
            Version = 1
        });

        context.AuditEvents.AddRange(
            Audit(customer.Id, "application.submitted", application.Id.ToString().ToUpperInvariant(), fixedTime),
            Audit(agent2.Id, "application.review.started", application.Id.ToString().ToUpperInvariant(), fixedTime),
            Audit(null, "auth.login.succeeded", null, fixedTime));

        await context.SaveChangesAsync();

        return new SeedData(
            sourceRoleIds,
            agent2.Id,
            product.Id,
            customerProfile.Id,
            garvitProfile.Id,
            application.Id,
            ticket.Id,
            fixedTime);
    }

    public static async Task<Dictionary<string, Guid>> SeedTargetRolesAsync(MigrationAppDbContext context)
    {
        var result = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in Roles.All)
        {
            var id = Guid.NewGuid();
            result[name] = id;
            context.Roles.Add(new IdentityRole<Guid>
            {
                Id = id,
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                ConcurrencyStamp = Guid.NewGuid().ToString()
            });
        }

        await context.SaveChangesAsync();
        return result;
    }

    public static Task SeedAppliedPostgresMigrationAsync(MigrationAppDbContext context)
    {
        context.Database.ExecuteSqlRaw(
            "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
            "\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL)");
        context.Database.ExecuteSqlRaw(
            "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
            $"VALUES ('{MigrationScope.PostgresMigrationId}', 'test')");
        return Task.CompletedTask;
    }

    public static async Task<ApplicationUser> SeedConflictAdminAsync(MigrationAppDbContext context)
    {
        var user = User(
            MigrationScope.ConflictAdminUserId,
            MigrationScope.AdminEmail,
            "Test User",
            new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero));
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    public static async Task AddConflictProfileAsync(MigrationAppDbContext context)
    {
        context.CustomerProfiles.Add(Profile(
            Guid.NewGuid(),
            MigrationScope.ConflictAdminUserId,
            "Conflict",
            "User",
            new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        await context.SaveChangesAsync();
    }

    private static ApplicationUser User(Guid id, string email, string fullName, DateTimeOffset fixedTime) => new()
    {
        Id = id,
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        PasswordHash = "test-only-placeholder",
        SecurityStamp = Guid.NewGuid().ToString(),
        ConcurrencyStamp = Guid.NewGuid().ToString(),
        PhoneNumberConfirmed = false,
        TwoFactorEnabled = false,
        LockoutEnabled = false,
        AccessFailedCount = 0,
        FullName = fullName,
        IsActive = true,
        CreatedAtUtc = fixedTime
    };

    private static IdentityUserRole<Guid> UserRole(Guid userId, Guid roleId) => new()
    {
        UserId = userId,
        RoleId = roleId
    };

    private static CustomerProfile Profile(Guid id, Guid userId, string first, string last, DateTimeOffset fixedTime) => new()
    {
        Id = id,
        UserId = userId,
        FirstName = first,
        LastName = last,
        PhoneNumber = "0000000000",
        DateOfBirth = new DateOnly(1990, 1, 1),
        AddressLine1 = "1 Test Street",
        City = "Testville",
        PostalCode = "00000",
        Country = "Testland",
        IsContactVerified = true,
        CreatedAtUtc = fixedTime,
        UpdatedAtUtc = fixedTime,
        Version = 1
    };

    private static AuditEvent Audit(Guid? actorId, string action, string? entityId, DateTimeOffset fixedTime) => new()
    {
        Id = Guid.NewGuid(),
        ActorUserId = actorId,
        Action = action,
        EntityType = "Application",
        EntityId = entityId,
        Succeeded = true,
        CreatedAtUtc = fixedTime,
        UpdatedAtUtc = fixedTime,
        Version = 1
    };
}
