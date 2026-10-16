using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;

namespace Northstar.Application.Tests;

/// <summary>Clock stub so time-dependent rules are deterministic.</summary>
internal sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Ambient caller identity. Defaults to an authenticated caller once a test assigns a user id.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public string? Email { get; set; } = "citizen@northstar.example";

    public bool IsAuthenticated => UserId is not null;

    public HashSet<string> Roles { get; } = new();

    public bool IsInRole(string role) => Roles.Contains(role);

    public string? IpAddress { get; set; } = "127.0.0.1";

    public string? UserAgent { get; set; } = "xunit";

    public string? CorrelationId { get; set; } = "test-correlation-id";
}

/// <summary>Captures audit actions so tests can assert on recorded behaviour.</summary>
internal sealed class TestAuditWriter : IAuditWriter
{
    public List<string> Actions { get; } = new();

    public Task RecordAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        bool succeeded = true,
        CancellationToken ct = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic reference generator so tests can assert exact values.</summary>
internal sealed class TestReferenceGenerator : IReferenceGenerator
{
    private int _applications;
    private int _tickets;

    public string NextApplicationReference() => $"NS-APP-T{++_applications:D6}";

    public string NextTicketReference() => $"NS-TKT-T{++_tickets:D6}";
}

/// <summary>User directory stub; tests register known agent ids.</summary>
internal sealed class TestUserDirectory : IUserDirectory
{
    public Dictionary<Guid, string> Users { get; } = new();

    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(Array.Empty<UserSummary>());

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Users.ContainsKey(userId));

    public Task<string?> GetEmailAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Users.TryGetValue(userId, out var email) ? email : null);

    public Task<bool> AssignRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct) =>
        Task.FromResult(true);

    public Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct) =>
        Task.FromResult(true);
}
