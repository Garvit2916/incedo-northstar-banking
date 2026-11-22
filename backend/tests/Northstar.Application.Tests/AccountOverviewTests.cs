using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

public sealed class AccountOverviewTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Customer_dashboard_returns_masked_account_context_and_sample_transactions()
    {
        var user = _fx.SeedUser();
        _fx.Caller.UserId = user.Id;
        _fx.Db.Products.Add(new Product
        {
            Code = "SAV-001",
            Name = "Everyday Savings Account",
            Summary = "Demo",
            Description = "Demo",
            Category = "Savings",
            Eligibility = "Demo",
            RequiredDocuments = new List<string> { "ProofOfIdentity" },
            Terms = "Demo",
            MinimumAge = 18,
            IsActive = true,
            DisplayOrder = 1,
            CreatedAtUtc = _fx.Clock.UtcNow,
            UpdatedAtUtc = _fx.Clock.UtcNow
        });
        await _fx.Db.SaveChangesAsync();

        var first = await _fx.Accounts.GetAsync(default);
        var second = await _fx.Accounts.GetAsync(default);

        Assert.Contains("••••", first.MaskedAccountNumber);
        Assert.Equal(first.MaskedAccountNumber, second.MaskedAccountNumber);
        Assert.DoesNotContain(user.Id.ToString(), first.MaskedAccountNumber);
        Assert.DoesNotContain(user.Id.ToString("N"), first.MaskedAccountNumber);
        Assert.Equal(4, first.Transactions.Count);
        Assert.All(first.Transactions, transaction =>
            Assert.Contains("sample", transaction.Narration.ToLowerInvariant()));
        Assert.Contains(first.ActiveProducts, product => product.ProductCode == "SAV-001");
        Assert.Contains("illustrative", first.DemoNotice.ToLowerInvariant());
    }

    [Fact]
    public async Task Account_overview_requires_an_authenticated_caller()
    {
        await Assert.ThrowsAsync<Common.ForbiddenException>(() => _fx.Accounts.GetAsync(default));
    }
}
