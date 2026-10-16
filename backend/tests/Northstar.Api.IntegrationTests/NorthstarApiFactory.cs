using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// Boots the real API (via <see cref="Program"/>) against an isolated SQLite
/// in-memory database so the full HTTP pipeline - middleware, authentication,
/// rate limiting, controllers, services and EF Core - is exercised end to end.
/// A single open connection keeps the shared in-memory database alive for the
/// lifetime of the factory; a unique name per factory instance isolates runs.
/// </summary>
public sealed class NorthstarApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _keepAlive;
    private readonly SemaphoreSlim _seedGate = new(1, 1);
    private bool _seeded;

    public NorthstarApiFactory()
    {
        ConnectionString =
            $"Data Source=northstar-api-tests-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        // The application reads configuration eagerly in its top-level statements
        // (before the host is built), so WebApplicationFactory.ConfigureAppConfiguration
        // - which applies at host-build time - is too late to influence things like
        // the database provider, email/AI adapters or rate limits. Environment
        // variables are a default configuration source available at
        // WebApplication.CreateBuilder time, so they reliably win over appsettings.json.
        var overrides = new Dictionary<string, string?>
        {
            ["Database__Provider"] = "Sqlite",
            ["ConnectionStrings__Default"] = ConnectionString,
            ["Seed__Enabled"] = "true",
            ["Seed__DefaultPassword"] = "Northstar#Dev01",
            ["Storage__Provider"] = "Local",
            ["Email__Provider"] = "Noop",
            ["Ai__Provider"] = "deterministic",
            ["RateLimiting__Auth__PermitLimit"] = "1000000",
            ["RateLimiting__Ai__PermitLimit"] = "1000000",
            ["RateLimiting__Uploads__PermitLimit"] = "1000000"
        };

        foreach (var (key, value) in overrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }

        // The host (and its background OutboxProcessor) starts as soon as the
        // factory builds the web application, which happens before seeding. The
        // schema must therefore exist beforehand, otherwise the processor's first
        // poll queries a database with no tables. Create it here against the same
        // shared in-memory database.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(ConnectionString)
            .Options;
        using var context = new AppDbContext(options, TimeProvider.System);
        context.Database.Migrate();
    }

    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    /// <summary>
    /// Applies migrations and seeds the synthetic demonstration data exactly as
    /// the developer experience does. Safe to call repeatedly; only runs once.
    /// </summary>
    public async Task EnsureSeededAsync()
    {
        if (_seeded)
        {
            return;
        }

        await _seedGate.WaitAsync();
        try
        {
            if (_seeded)
            {
                return;
            }

            var logger = Services.GetRequiredService<ILoggerFactory>()
                .CreateLogger("IntegrationTestSeed");
            var configuration = Services.GetRequiredService<IConfiguration>();

            await DbInitializer.InitializeAsync(Services, configuration, logger);
            _seeded = true;
        }
        finally
        {
            _seedGate.Release();
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _seedGate.Dispose();
            _keepAlive.Dispose();
        }
    }
}

/// <summary>Shares a single seeded factory (and therefore one database) across all test classes.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<NorthstarApiFactory>
{
    public const string Name = "api";
}
