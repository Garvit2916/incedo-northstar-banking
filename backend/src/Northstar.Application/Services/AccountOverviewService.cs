using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;

namespace Northstar.Application.Services;

/// <summary>
/// Builds a deterministic, clearly labelled demo account overview. No core
/// banking system is connected; identifiers are derived from a one-way hash of
/// the user id and are always masked before leaving the application layer.
/// </summary>
public sealed class AccountOverviewService(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock)
{
    private const string DemoNotice =
        "Demo account view: balances and transactions are illustrative and are not connected to a real bank account.";

    public async Task<AccountOverviewResponse> GetAsync(CancellationToken ct)
    {
        var userId = currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
        var suffix = DerivedSuffix(userId);

        var approved = await db.Applications
            .AsNoTracking()
            .Where(a => a.CustomerProfile!.UserId == userId && a.Status == ApplicationStatus.Approved)
            .Select(a => new ActiveProductResponse(
                a.Product != null ? a.Product.Name : "Northstar product",
                a.Product != null ? a.Product.Code : string.Empty,
                a.Reference,
                a.Status.ToString()))
            .ToListAsync(ct);

        if (approved.Count == 0)
        {
            var savings = await db.Products
                .AsNoTracking()
                .Where(p => p.Code == "SAV-001")
                .Select(p => new ActiveProductResponse(p.Name, p.Code, "ILLUSTRATIVE", "Demo product view"))
                .FirstOrDefaultAsync(ct);

            if (savings is not null)
            {
                approved.Add(savings);
            }
        }

        var now = clock.UtcNow;
        var transactions = new[]
        {
            new AccountTransactionResponse("DEMO-TXN-001", now.AddDays(-1), "Salary credit (sample)", "Income", 4250.00m, "Credit"),
            new AccountTransactionResponse("DEMO-TXN-002", now.AddDays(-3), "Card payment (sample)", "Card", -128.40m, "Debit"),
            new AccountTransactionResponse("DEMO-TXN-003", now.AddDays(-5), "UPI transfer (sample)", "Payments", -64.00m, "Debit"),
            new AccountTransactionResponse("DEMO-TXN-004", now.AddDays(-8), "Savings interest (sample)", "Interest", 12.75m, "Credit")
        };

        return new AccountOverviewResponse(
            "Everyday savings",
            "Savings",
            $"•••• •••• {suffix}",
            $"NSTD0•••{suffix[..2]}",
            "Northstar Demo Branch",
            "INR",
            84250.75m,
            transactions,
            approved,
            DemoNotice);
    }

    private static string DerivedSuffix(Guid userId)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(userId.ToString("N")));
        var numeric = ((hash[0] << 8) | hash[1]) % 10000;
        return numeric.ToString("0000");
    }
}
