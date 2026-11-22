using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using Northstar.Application.Configuration;
using Northstar.Infrastructure.Ai;
using Northstar.Infrastructure.Auditing;
using Northstar.Infrastructure.Common;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Notifications;
using Northstar.Infrastructure.Outbox;
using Northstar.Infrastructure.Persistence;
using Northstar.Infrastructure.Scanning;
using Northstar.Infrastructure.Storage;

namespace Northstar.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.TryAddTimeProvider();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        AddDatabase(services, configuration);
        AddIdentity(services);
        AddStorage(services, configuration, environment);
        AddNotifications(services, configuration);
        AddAi(services, configuration);

        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<IDateTimeProvider, SystemDateTimeProvider>();
        services.AddScoped<IReferenceGenerator, ReferenceGenerator>();
        services.AddScoped<IAuditWriter, AuditWriter>();
        services.AddScoped<IFileScanner, DevelopmentFileScanner>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<TokenService>();

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddHostedService<OutboxProcessor>();

        return services;
    }

    private static void TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }
    }

    private static void AddDatabase(IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"] ?? "Sqlite";
        var connectionString = configuration.GetConnectionString("Default") ?? "Data Source=northstar.db";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connectionString, sql =>
                    sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            }
            else
            {
                options.UseSqlite(connectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));
            }
        });
    }

    private static void AddIdentity(IServiceCollection services)
    {
        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                // Mirrors Northstar.Application.Validation.PasswordPolicy.
                options.Password.RequiredLength = 10;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireDigit = true;
                options.Password.RequireNonAlphanumeric = true;
                options.User.RequireUniqueEmail = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.AllowedForNewUsers = true;
            })
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<AppDbContext>()
            .AddDefaultTokenProviders();
    }

    private static void AddStorage(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<StorageOptions>(configuration.GetSection(StorageOptions.SectionName));
        services.Configure<FileUploadOptions>(configuration.GetSection(FileUploadOptions.SectionName));
        services.PostConfigure<FileUploadOptions>(options => options.ApplyFallbacks());

        services.AddScoped<IBlobStorage>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<StorageOptions>>().Value;
            if (options.Provider.Equals("Azure", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(options.AzureConnectionString))
            {
                return new AzureBlobStorage(options.AzureConnectionString, options.AzureContainerName);
            }

            var root = Path.IsPathRooted(options.LocalRootPath)
                ? options.LocalRootPath
                : Path.Combine(environment.ContentRootPath, options.LocalRootPath);

            return new FileSystemBlobStorage(root);
        });
    }

    private static void AddNotifications(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));

        services.AddScoped<IEmailSender>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<EmailOptions>>().Value;
            return options.Provider.ToLowerInvariant() switch
            {
                "smtp" => ActivatorUtilities.CreateInstance<SmtpEmailSender>(sp),
                "azurecommunication" => ActivatorUtilities.CreateInstance<AzureCommunicationEmailSender>(sp),
                _ => ActivatorUtilities.CreateInstance<NoopEmailSender>(sp)
            };
        });
    }

    private static void AddAi(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AiProviderOptions>(configuration.GetSection(AiProviderOptions.SectionName));

        services.AddScoped<DeterministicFallbackAiProvider>();

        var options = configuration.GetSection(AiProviderOptions.SectionName).Get<AiProviderOptions>()
                      ?? new AiProviderOptions();

        var provider = options.Provider.Trim().ToLowerInvariant();
        var liveConfigured =
            (provider is "openai-compatible" or "azure-openai") &&
            !string.IsNullOrWhiteSpace(options.BaseUrl) &&
            !string.IsNullOrWhiteSpace(options.ApiKey);

        if (provider == "bedrock")
        {
            services.AddScoped<AmazonBedrockAiProvider>();
            services.AddScoped<IAiProvider>(sp => new ResilientAiProvider(
                sp.GetRequiredService<AmazonBedrockAiProvider>(),
                sp.GetRequiredService<DeterministicFallbackAiProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientAiProvider>>()));
        }
        else if (liveConfigured)
        {
            services.AddHttpClient<OpenAiCompatibleAiProvider>((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<AiProviderOptions>>().Value;
                http.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
                http.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", opts.ApiKey);
                http.DefaultRequestHeaders.Add("api-key", opts.ApiKey);
            });

            services.AddScoped<IAiProvider>(sp => new ResilientAiProvider(
                sp.GetRequiredService<OpenAiCompatibleAiProvider>(),
                sp.GetRequiredService<DeterministicFallbackAiProvider>(),
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ResilientAiProvider>>()));
        }
        else
        {
            services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<DeterministicFallbackAiProvider>());
        }
    }
}
