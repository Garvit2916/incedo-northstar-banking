using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using Northstar.Api.Middleware;
using Northstar.Application;
using Northstar.Infrastructure;
using Northstar.Infrastructure.Identity;
using Northstar.Infrastructure.Persistence;

const string DevelopmentSigningKey = "development-only-signing-key-change-me-please-0123456789";

var builder = WebApplication.CreateBuilder(args);

// JWT signing key must be supplied via configuration or a managed secret store in
// non-development environments. In development we inject a well-known key into the
// configuration so the token issuer and the bearer validator share the same value.
// A key set here (rather than on a local object) is visible to IOptions<JwtOptions>.
var configuredSigningKey = builder.Configuration["Jwt:SigningKey"];
var isLocal = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");

// ECS sends stdout/stderr to CloudWatch. JSON logs keep structured fields queryable.
if (!isLocal)
{
    builder.Logging.AddJsonConsole(options =>
    {
        options.IncludeScopes = true;
        options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
    });
}

if (string.IsNullOrWhiteSpace(configuredSigningKey) || Encoding.UTF8.GetByteCount(configuredSigningKey) < 32)
{
    if (!isLocal)
    {
        throw new InvalidOperationException(
            "Jwt:SigningKey must be supplied via configuration or a managed secret store " +
            "and must be at least 32 bytes long.");
    }

    builder.Configuration["Jwt:SigningKey"] = DevelopmentSigningKey;
}

// ---- Application + infrastructure ------------------------------------------
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);

// ---- Authentication (JWT bearer) -------------------------------------------
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = ClaimTypes.Name,
            RoleClaimType = ClaimTypes.Role
        };
    });

builder.Services.AddAuthorization();

// The API sits behind the nginx sidecar inside the same Fargate task. Forwarding
// only from loopback preserves the real client IP for rate limiting and audit.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// ---- Rate limiting ----------------------------------------------------------
// Limits are configuration-driven so environments (including automated tests)
// can tune them without code changes; the built-in defaults stay tight.
var rateLimits = builder.Configuration.GetSection("RateLimiting");
var authPermit = rateLimits.GetValue("Auth:PermitLimit", 10);
var authWindowSeconds = rateLimits.GetValue("Auth:WindowSeconds", 60);
var aiPermit = rateLimits.GetValue("Ai:PermitLimit", 30);
var aiWindowSeconds = rateLimits.GetValue("Ai:WindowSeconds", 60);
var uploadPermit = rateLimits.GetValue("Uploads:PermitLimit", 20);
var uploadWindowSeconds = rateLimits.GetValue("Uploads:WindowSeconds", 300);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = authPermit,
                Window = TimeSpan.FromSeconds(authWindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("ai", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = aiPermit,
                Window = TimeSpan.FromSeconds(aiWindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("uploads", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? context.Connection.RemoteIpAddress?.ToString()
                ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = uploadPermit,
                Window = TimeSpan.FromSeconds(uploadWindowSeconds),
                QueueLimit = 0
            }));
});

// ---- CORS -------------------------------------------------------------------
var allowedOrigins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>()
                     ?? new[] { "http://localhost:5173" };

builder.Services.AddCors(options =>
    options.AddPolicy("spa", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()));

// ---- Health checks ----------------------------------------------------------
builder.Services.AddHealthChecks()
    .AddDbContextCheck<AppDbContext>("database");

// ---- MVC / OpenAPI / errors -------------------------------------------------
builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Northstar Banking Onboarding & Support API",
        Version = "v1",
        Description = "Secure banking onboarding and AI-assisted customer support. " +
                      "Independent assessment project; not an official Incedo product."
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter the JWT access token returned by /api/auth/login."
    });

    options.AddSecurityRequirement(_ => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer"), new List<string>() }
    });
});

builder.Services.AddProblemDetails();

var app = builder.Build();

// ---- Pipeline ---------------------------------------------------------------
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!isLocal)
{
    // A minimal HTTP-only Fargate deployment has no TLS listener. HSTS and HTTPS
    // redirection are opt-in so the demo does not lock browsers into HTTPS.
    if (builder.Configuration.GetValue("Security:EnableHsts", false))
    {
        app.UseHsts();
    }

    if (builder.Configuration.GetValue("Security:RequireHttps", false))
    {
        app.UseHttpsRedirection();
    }
}

app.UseForwardedHeaders();
app.UseCors("spa");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");

await InitializeDatabaseAsync(app, builder.Configuration);

app.Run();

static async Task InitializeDatabaseAsync(WebApplication app, IConfiguration configuration)
{
    if (app.Environment.IsEnvironment("Testing"))
    {
        return;
    }

    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
    try
    {
        // Migrations and demonstration seeding are independent so production can
        // apply schema changes without creating the well-known demo accounts.
        if (configuration.GetValue("Database:ApplyMigrations", true))
        {
            await DbInitializer.MigrateAsync(app.Services, logger);
        }

        if (configuration.GetValue("Seed:Enabled", false))
        {
            await DbInitializer.SeedAsync(app.Services, configuration, logger);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Database initialisation failed.");
        throw;
    }
}

/// <summary>Exposed for the integration test host.</summary>
public partial class Program;
