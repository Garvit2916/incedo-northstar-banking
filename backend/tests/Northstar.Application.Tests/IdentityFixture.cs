using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Application.Tests;

/// <summary>
/// Isolated in-memory Identity stack for tests that exercise role bootstrapping
/// and registration failure handling against the real EF Core provider.
/// </summary>
internal sealed class IdentityFixture : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public IdentityFixture(bool failingRoleAssignment = false)
    {
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

        services
            .AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>();

        if (failingRoleAssignment)
        {
            services.AddScoped<UserManager<ApplicationUser>, FailingRoleUserManager>();
        }

        Provider = services.BuildServiceProvider();
    }

    public IServiceProvider Provider { get; }

    public async Task InitializeAsync()
    {
        using var scope = Provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public void Dispose()
    {
        (Provider as IDisposable)?.Dispose();
        _connection.Dispose();
    }

    /// <summary>
    /// Forces the <see cref="UserManager{TUser}.AddToRoleAsync"/> failure-result
    /// path without changing production code or the underlying user store.
    /// </summary>
    private sealed class FailingRoleUserManager : UserManager<ApplicationUser>
    {
        public FailingRoleUserManager(
            IUserStore<ApplicationUser> store,
            IOptions<IdentityOptions> optionsAccessor,
            IPasswordHasher<ApplicationUser> passwordHasher,
            IEnumerable<IUserValidator<ApplicationUser>> userValidators,
            IEnumerable<IPasswordValidator<ApplicationUser>> passwordValidators,
            ILookupNormalizer keyNormalizer,
            IdentityErrorDescriber errors,
            IServiceProvider services,
            ILogger<UserManager<ApplicationUser>> logger)
            : base(
                store,
                optionsAccessor,
                passwordHasher,
                userValidators,
                passwordValidators,
                keyNormalizer,
                errors,
                services,
                logger)
        {
        }

        public override Task<IdentityResult> AddToRoleAsync(ApplicationUser user, string role) =>
            Task.FromResult(IdentityResult.Failed(
                new IdentityError
                {
                    Code = "RoleAssignmentFailed",
                    Description = "Role assignment rejected."
                }));
    }
}
