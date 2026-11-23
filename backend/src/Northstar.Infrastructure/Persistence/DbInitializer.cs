using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Northstar.Domain;
using Northstar.Domain.Entities;
using Northstar.Infrastructure.Identity;

namespace Northstar.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and seeds synthetic demonstration data. All seed content is
/// fictitious; no real customer information is present. Seeding is idempotent so
/// repeated startups do not duplicate records.
/// </summary>
public static class DbInitializer
{
    private const string DefaultPassword = "Northstar#Dev01";

    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        await MigrateAsync(services, logger, ct);
        await SeedAsync(services, configuration, logger, ct);
    }

    /// <summary>
    /// Applies pending EF Core migrations without creating demonstration data.
    /// Hosted deployments call this independently of <see cref="SeedAsync"/>.
    /// </summary>
    public static async Task MigrateAsync(
        IServiceProvider services,
        ILogger logger,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        logger.LogInformation("Applying database migrations...");
        await db.Database.MigrateAsync(ct);
    }

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        await SeedRolesAsync(roleManager, logger);
        await SeedUsersAsync(userManager, db, configuration, logger, ct);
        await SeedProductsAsync(db, logger, ct);
        await DeactivateSmokeTestProductsAsync(db, logger, ct);
        await SeedKnowledgeArticlesAsync(db, logger, ct);
        await SeedTicketsAsync(db, userManager, logger, ct);
        await SeedDemoApplicationAsync(db, userManager, logger, ct);
    }

    private static async Task SeedRolesAsync(RoleManager<IdentityRole<Guid>> roleManager, ILogger logger)
    {
        foreach (var role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole<Guid>(role));
                logger.LogInformation("Seeded role {Role}.", role);
            }
        }
    }

    private static async Task SeedUsersAsync(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        IConfiguration configuration,
        ILogger logger,
        CancellationToken ct)
    {
        var password = configuration["Seed:DefaultPassword"] ?? DefaultPassword;

        await EnsureUserAsync(userManager, db, "admin@northstar.example", "Alex Admin", Roles.Administrator, password, logger, ct);
        var reviewer = await EnsureUserAsync(userManager, db, "reviewer@northstar.example", "Rita Reviewer", Roles.Reviewer, password, logger, ct);
        await EnsureUserAsync(userManager, db, "agent@northstar.example", "Sam Agent", Roles.SupportAgent, password, logger, ct);
        await EnsureUserAsync(userManager, db, "customer@northstar.example", "Casey Customer", Roles.Customer, password, logger, ct);

        // A second reviewer improves the demonstration of assignment.
        await EnsureUserAsync(userManager, db, "reviewer2@northstar.example", "Rob Reviewer", Roles.Reviewer, password, logger, ct);
        logger.LogInformation("Reviewer seed account: {Email}", reviewer?.Email);
    }

    private static async Task<ApplicationUser?> EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        string email,
        string fullName,
        string role,
        string password,
        ILogger logger,
        CancellationToken ct)
    {
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                FullName = fullName,
                IsActive = true,
                EmailConfirmed = true,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };

            var result = await userManager.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                logger.LogError("Failed to seed user {Email}: {Errors}", email,
                    string.Join("; ", result.Errors.Select(e => e.Description)));
                return null;
            }

            await userManager.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded user {Email} with role {Role}.", email, role);
        }

        if (role == Roles.Customer && !await db.CustomerProfiles.AnyAsync(p => p.UserId == user.Id, ct))
        {
            db.CustomerProfiles.Add(new CustomerProfile
            {
                UserId = user.Id,
                FirstName = fullName.Split(' ')[0],
                LastName = fullName.Split(' ').Last(),
                PhoneNumber = "+1-555-0100",
                DateOfBirth = new DateOnly(1990, 5, 12),
                AddressLine1 = "1 Demo Street",
                City = "Demo City",
                PostalCode = "10001",
                Country = "United States",
                Occupation = "Analyst",
                AnnualIncomeBand = "50k-100k",
                IsContactVerified = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }

        return user;
    }

    private static async Task SeedProductsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var definitions = new[]
        {
            new Product
            {
                Code = "SAV-001",
                Name = "Everyday Savings Account",
                Summary = "A straightforward savings account for day-to-day banking (demonstration product).",
                Description = "Illustrative savings account used for the assessment demonstration. " +
                              "No real rates or terms are represented by this content.",
                Category = "Savings",
                Eligibility = "Available to applicants aged 18 or over with a verified address (demonstration rule).",
                RequiredDocuments = new List<string> { "ProofOfIdentity", "ProofOfAddress" },
                Terms = "Demonstration terms only. Not a financial offer.",
                MinimumAge = 18,
                IsActive = true,
                DisplayOrder = 1
            },
            new Product
            {
                Code = "CC-001",
                Name = "Everyday Credit Card (Demo)",
                Summary = "An illustrative credit card with transparent, demo-only terms.",
                Description = "Demonstration credit card product. No credit decision, interest rate or real limit is represented.",
                Category = "Cards",
                Eligibility = "Available to applicants aged 21 or over with a verified address (demonstration rule).",
                RequiredDocuments = new List<string> { "ProofOfIdentity", "ProofOfAddress", "ProofOfIncome" },
                Terms = "Demonstration terms only. Not a financial offer.",
                MinimumAge = 21,
                IsActive = true,
                DisplayOrder = 2
            },
            new Product
            {
                Code = "FD-001",
                Name = "Fixed Deposit (Demo)",
                Summary = "A fixed-term deposit product for the demonstration (no real rates).",
                Description = "Illustrative fixed deposit. Terms and any rates shown in this catalogue are fictional.",
                Category = "Deposits",
                Eligibility = "Available to applicants aged 18 or over (demonstration rule).",
                RequiredDocuments = new List<string> { "ProofOfIdentity", "ProofOfAddress", "ProofOfIncome" },
                Terms = "Demonstration terms only. Not a financial offer.",
                MinimumAge = 18,
                IsActive = true,
                DisplayOrder = 3
            },
            new Product
            {
                Code = "PERS-001",
                Name = "Personal Loan (Demo)",
                Summary = "An illustrative personal loan application with no real credit decision.",
                Description = "Demonstration personal loan. Eligibility, rates and limits are fictional.",
                Category = "Loans",
                Eligibility = "Available to applicants aged 21 or over with a verified address (demonstration rule).",
                RequiredDocuments = new List<string> { "ProofOfIdentity", "ProofOfAddress", "ProofOfIncome" },
                Terms = "Demonstration terms only. Not a financial offer.",
                MinimumAge = 21,
                IsActive = true,
                DisplayOrder = 4
            }
        };

        var added = 0;
        foreach (var definition in definitions)
        {
            var existing = await db.Products.FirstOrDefaultAsync(p => p.Code == definition.Code, ct);
            if (existing is not null)
            {
                continue;
            }

            definition.Id = Guid.NewGuid();
            definition.CreatedAtUtc = DateTimeOffset.UtcNow;
            definition.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.Products.Add(definition);
            added++;
        }

        // Earlier demo builds included a wealth product outside the requested
        // product set. Keep historical rows for data continuity but hide them.
        var legacy = await db.Products
            .Where(p => p.Code == "WM-001" && p.IsActive)
            .ToListAsync(ct);
        foreach (var product in legacy)
        {
            product.IsActive = false;
        }

        if (added > 0 || legacy.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Updated demonstration product catalogue: {Added} added, {Legacy} retired.", added, legacy.Count);
        }
    }

    private static async Task SeedKnowledgeArticlesAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken ct)
    {
        var existingCodes = await db.KnowledgeArticles
            .Select(a => a.ArticleCode)
            .ToListAsync(ct);
        var known = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);

        var added = 0;
        var now = DateTimeOffset.UtcNow;
        foreach (var article in KnowledgeSeedData.Articles)
        {
            if (!known.Add(article.ArticleCode))
            {
                continue;
            }

            article.CreatedAtUtc = now;
            article.UpdatedAtUtc = now;
            db.KnowledgeArticles.Add(article);
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Seeded {Count} demonstration knowledge articles.", added);
        }
    }

    /// <summary>
    /// The regression suite (<c>scripts/smoke.ps1</c>) creates products with a
    /// <c>TST</c> code prefix to prove the admin create path. Those rows persist
    /// in the file database between runs, so any that were left active are
    /// deactivated here to keep the customer-facing catalogue clean. This is
    /// idempotent and never runs in vain when there is nothing to clean up.
    /// </summary>
    private static async Task DeactivateSmokeTestProductsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var leftovers = await db.Products
            .Where(p => p.IsActive && p.Code.StartsWith("TST"))
            .ToListAsync(ct);

        if (leftovers.Count == 0)
        {
            return;
        }

        foreach (var product in leftovers)
        {
            product.IsActive = false;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Deactivated {Count} leftover smoke-test product(s).", leftovers.Count);
    }

    private static async Task SeedTicketsAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken ct)
    {
        var customer = await userManager.FindByEmailAsync("customer@northstar.example");
        var agent = await userManager.FindByEmailAsync("agent@northstar.example");
        var now = DateTimeOffset.UtcNow;

        Ticket Make(string reference, string subject, string description, TicketCategory category,
            TicketPriority priority, TicketStatus status, params (MessageAuthorType Author, string Body)[] messages)
        {
            var ticket = new Ticket
            {
                Reference = reference,
                Subject = subject,
                Description = description,
                Category = category,
                Priority = priority,
                CustomerUserId = customer?.Id,
                SlaDueAtUtc = now.AddHours(24),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            ticket.ChangeStatus(status, now);

            foreach (var (author, body) in messages)
            {
                ticket.Messages.Add(new TicketMessage
                {
                    TicketId = ticket.Id,
                    AuthorType = author,
                    AuthorUserId = author == MessageAuthorType.Agent ? agent?.Id : customer?.Id,
                    Body = body,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }

            return ticket;
        }

        var candidates = new[]
        {
            Make("NS-TKT-1001", "Cannot sign in to my account",
                "I have been locked out of my account since this morning and cannot reset my password.",
                TicketCategory.AccountAccess, TicketPriority.High, TicketStatus.Open,
                (MessageAuthorType.Customer, "I am locked out and cannot reset my password. Please help urgently."),
                (MessageAuthorType.Agent, "I have escalated this to our secure recovery process.")),

            Make("NS-TKT-1002", "Application still showing as under review",
                "My savings application has been under review for a while and I would like an update.",
                TicketCategory.ApplicationDelay, TicketPriority.Normal, TicketStatus.New,
                (MessageAuthorType.Customer, "It has been over a week and my application is still under review. Is anything wrong?")),

            Make("NS-TKT-1003", "Which document do I need to upload?",
                "The portal says a document is outstanding but I am not sure which one is required.",
                TicketCategory.MissingDocuments, TicketPriority.Normal, TicketStatus.New,
                (MessageAuthorType.Customer, "The portal says a document is missing but does not say which. Can you tell me?")),

            Make("NS-TKT-1004", "Did not receive my status email",
                "I was told I would receive an email update but I have not seen one.",
                TicketCategory.NotificationIssue, TicketPriority.Low, TicketStatus.PendingCustomer,
                (MessageAuthorType.Customer, "I have not received any emails about my application.")),

            Make("NS-TKT-1005", "How do I check my application reference?",
                "I cannot find where to see my application reference number.",
                TicketCategory.StatusEnquiry, TicketPriority.Low, TicketStatus.Resolved,
                (MessageAuthorType.Customer, "Where do I find my reference number?"),
                (MessageAuthorType.Agent, "Shared the tracking article and stepped the customer through it.")),

            Make("NS-TKT-1006", "Card charge I do not recognise",
                "There is a card charge of 2,499 from an unknown merchant that I did not make.",
                TicketCategory.CardTransactionDispute, TicketPriority.High, TicketStatus.Open,
                (MessageAuthorType.Customer, "I see a wrong charge of 2,499 on my credit card. I did not make this transaction.")),

            Make("NS-TKT-1007", "UPI payment failed but money was deducted",
                "My UPI payment failed but the money was taken from my account.",
                TicketCategory.FailedUpiTransaction, TicketPriority.High, TicketStatus.New,
                (MessageAuthorType.Customer, "The UPI payment failed, money was deducted and I have no refund yet.")),

            Make("NS-TKT-1008", "Lost card - please block it",
                "I have lost my credit card and want it blocked safely.",
                TicketCategory.CardBlockingReplacement, TicketPriority.Urgent, TicketStatus.New,
                (MessageAuthorType.Customer, "I lost my card today. Please block it and tell me how to get a replacement.")),

            Make("NS-TKT-1009", "KYC document was rejected",
                "My proof of address was rejected and I do not know how to re-upload it.",
                TicketCategory.KycVerification, TicketPriority.Normal, TicketStatus.New,
                (MessageAuthorType.Customer, "My KYC document is rejected. Where do I upload a new one?")),

            Make("NS-TKT-1010", "Question about my credit card bill",
                "I would like to understand the minimum due and due date on my card statement.",
                TicketCategory.CreditCardBilling, TicketPriority.Low, TicketStatus.PendingCustomer,
                (MessageAuthorType.Customer, "Can you explain the minimum due on my credit card statement?"))
        };

        var existing = await db.Tickets
            .Select(t => t.Reference)
            .ToListAsync(ct);
        var known = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var missing = candidates.Where(t => known.Add(t.Reference)).ToList();

        if (missing.Count == 0)
        {
            return;
        }

        db.Tickets.AddRange(missing);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} demonstration support ticket(s).", missing.Count);
    }

    private static async Task SeedDemoApplicationAsync(
        AppDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger logger,
        CancellationToken ct)
    {
        var customer = await userManager.FindByEmailAsync("customer@northstar.example");
        if (customer is null)
        {
            return;
        }

        var profile = await db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == customer.Id, ct);
        var product = await db.Products.FirstOrDefaultAsync(p => p.Code == "SAV-001", ct);
        if (profile is null || product is null)
        {
            return;
        }

        if (await db.Applications.AnyAsync(a => a.CustomerProfileId == profile.Id, ct))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var application = Domain.Entities.Application.CreateDraft(
            profile.Id,
            product.Id,
            "NS-APP-DEMO01",
            "{\"employmentStatus\":\"Employed\",\"purpose\":\"Everyday banking\"}");

        application.CreatedAtUtc = now;
        application.UpdatedAtUtc = now;
        application.Submit(customer.Id, now);

        db.Applications.Add(application);
        db.Notifications.Add(new Notification
        {
            UserId = customer.Id,
            ApplicationId = application.Id,
            Type = NotificationType.ApplicationSubmitted,
            Title = "Application submitted",
            Body = $"Your application {application.Reference} has been submitted and will be reviewed.",
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demonstration application {Reference}.", application.Reference);
    }
}
