using Microsoft.AspNetCore.Identity;

namespace Northstar.Infrastructure.Identity;

/// <summary>
/// Identity user for the platform. Password hashing, lockout, and token handling
/// are provided by ASP.NET Core Identity; no custom cryptography is implemented.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = string.Empty;

    /// <summary>
    /// Soft enable/disable flag managed by administrators. Disabled users cannot
    /// authenticate but their records and audit history are retained.
    /// </summary>
    public bool IsActive { get; set; } = true;

    public DateTimeOffset CreatedAtUtc { get; set; }
}
