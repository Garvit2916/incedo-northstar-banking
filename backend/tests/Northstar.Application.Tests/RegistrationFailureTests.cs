using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Northstar.Application.Contracts;
using Northstar.Application.Validation;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Application.Tests;

public sealed class RegistrationFailureTests
{
    [Fact]
    public async Task RegisterAsync_removes_new_user_when_role_assignment_fails()
    {
        using var fixture = new IdentityFixture(failingRoleAssignment: true);
        await fixture.InitializeAsync();

        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = new TestAuditWriter();
        var auth = BuildAuthService(scope.ServiceProvider, db, audit);

        var result = await auth.RegisterAsync(ValidRequest(), CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Role assignment rejected.", result.Errors ?? Array.Empty<string>());
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.CustomerProfiles.ToListAsync());
        Assert.Empty(audit.Actions);
    }

    [Fact]
    public async Task RegisterAsync_removes_new_user_and_preserves_missing_role_exception()
    {
        using var fixture = new IdentityFixture();
        await fixture.InitializeAsync();

        using var scope = fixture.Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = new TestAuditWriter();
        var auth = BuildAuthService(scope.ServiceProvider, db, audit);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => auth.RegisterAsync(ValidRequest(), CancellationToken.None));

        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.CustomerProfiles.ToListAsync());
        Assert.Empty(audit.Actions);
    }

    private static AuthService BuildAuthService(
        IServiceProvider services,
        AppDbContext db,
        TestAuditWriter audit)
    {
        var jwt = Options.Create(new JwtOptions
        {
            Issuer = "northstar-tests",
            Audience = "northstar-test-clients",
            SigningKey = "unit-test-signing-key-0123456789abcdef"
        });

        return new AuthService(
            services.GetRequiredService<UserManager<ApplicationUser>>(),
            db,
            new TokenService(jwt),
            new TestClock(),
            new TestCurrentUser(),
            audit,
            new RegisterRequestValidator(),
            new LoginRequestValidator(),
            NullLogger<AuthService>.Instance);
    }

    private static RegisterRequest ValidRequest() =>
        new(
            Email: "new.customer@northstar.example",
            Password: "Valid#12345",
            FirstName: "Ada",
            LastName: "Lovelace",
            PhoneNumber: null,
            ConfirmPassword: "Valid#12345");
}
