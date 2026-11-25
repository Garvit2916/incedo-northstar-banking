using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Northstar.Domain;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Application.Tests;

public sealed class RoleInitializationTests
{
    [Fact]
    public async Task EnsureRolesAsync_creates_only_the_canonical_roles()
    {
        using var fixture = new IdentityFixture();
        await fixture.InitializeAsync();

        await DbInitializer.EnsureRolesAsync(fixture.Provider, NullLogger.Instance);

        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var names = await db.Roles.Select(r => r.Name).ToListAsync();
        Assert.Equal(Roles.All.Count, names.Count);
        Assert.All(Roles.All, role => Assert.Contains(role, names));

        Assert.Empty(db.Users);
        Assert.Empty(db.Products);
        Assert.Empty(db.CustomerProfiles);
        Assert.Empty(db.KnowledgeArticles);
        Assert.Empty(db.Tickets);
    }

    [Fact]
    public async Task EnsureRolesAsync_is_idempotent()
    {
        using var fixture = new IdentityFixture();
        await fixture.InitializeAsync();

        await DbInitializer.EnsureRolesAsync(fixture.Provider, NullLogger.Instance);

        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(Roles.All.Count, await db.Roles.CountAsync());
        }

        await DbInitializer.EnsureRolesAsync(fixture.Provider, NullLogger.Instance);

        using (var scope = fixture.Provider.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(Roles.All.Count, await db.Roles.CountAsync());
            foreach (var role in Roles.All)
            {
                Assert.Single(await db.Roles.Where(r => r.Name == role).ToListAsync());
            }
        }
    }
}
