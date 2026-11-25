using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Northstar.Application.Abstractions;
using Northstar.Infrastructure;
using Northstar.Infrastructure.Notifications;
using Northstar.Infrastructure.Persistence;
using Northstar.Infrastructure.Storage;

namespace Northstar.Application.Tests;

/// <summary>
/// Verifies that provider selection stays configuration-driven and that the
/// local defaults do not require any AWS SDK client or credential.
/// </summary>
public sealed class DependencyInjectionTests
{
    [Fact]
    public void Default_configuration_preserves_local_storage_and_noop_events()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        Assert.IsType<FileSystemBlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<NoopNotificationEventPublisher>(
            provider.GetRequiredService<INotificationEventPublisher>());
        Assert.Null(provider.GetService<Amazon.S3.IAmazonS3>());
        Assert.Null(provider.GetService<Amazon.SimpleNotificationService.IAmazonSimpleNotificationService>());
    }

    [Fact]
    public void Aws_configuration_registers_s3_and_sns_providers()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Storage:Provider"] = "S3",
            ["Storage:S3BucketName"] = "northstar-kyc-test",
            ["Storage:S3Region"] = "us-east-1",
            ["Sns:TopicArn"] = "arn:aws:sns:us-east-1:123456789012:northstar-events",
            ["Sns:Region"] = "us-east-1"
        });

        Assert.IsType<S3BlobStorage>(provider.GetRequiredService<IBlobStorage>());
        Assert.IsType<SnsNotificationEventPublisher>(
            provider.GetRequiredService<INotificationEventPublisher>());
    }

    [Fact]
    public void Default_configuration_uses_sqlite_and_existing_migrations()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());
        var db = provider.GetRequiredService<AppDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite", db.Database.ProviderName);
        AssertMigrationsComeFromAssembly(db, typeof(AppDbContext).Assembly.GetName().Name!);
    }

    [Theory]
    [InlineData("Postgres")]
    [InlineData("postgresql")]
    public void Postgres_configuration_uses_npgsql_and_postgres_migrations(string databaseProvider)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Database:Provider"] = databaseProvider,
            ["ConnectionStrings:Default"] = "Host=localhost;Database=northstar;Username=postgres"
        });

        var db = provider.GetRequiredService<AppDbContext>();

        Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
        AssertMigrationsComeFromAssembly(
            db,
            "Northstar.Infrastructure.Persistence.PostgresMigrations");
        Assert.Null(provider.GetService<Amazon.S3.IAmazonS3>());
        Assert.Null(provider.GetService<Amazon.SimpleNotificationService.IAmazonSimpleNotificationService>());
    }

    [Theory]
    [InlineData("Postgres", "Data Source=northstar.db")]
    [InlineData("Unknown", "Host=localhost;Database=northstar;Username=postgres")]
    public void Invalid_database_configuration_fails_fast(
        string databaseProvider,
        string connectionString)
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Database:Provider"] = databaseProvider,
            ["ConnectionStrings:Default"] = connectionString
        });

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AppDbContext>());
    }

    private static void AssertMigrationsComeFromAssembly(AppDbContext db, string expectedAssembly)
    {
        var migrations = db.GetService<IMigrationsAssembly>().Migrations;

        Assert.NotEmpty(migrations);
        Assert.All(migrations.Values, migration =>
            Assert.Equal(expectedAssembly, migration.Assembly.GetName().Name));
    }

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration, new TestHostEnvironment());
        return services.BuildServiceProvider();
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";

        public string ApplicationName { get; set; } = "Northstar.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
