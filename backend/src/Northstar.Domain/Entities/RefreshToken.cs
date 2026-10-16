using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// A rotating refresh token. Only a hash of the token is stored so that a
/// database disclosure cannot be replayed directly. Rotation links each token to
/// its successor to support reuse detection.
/// </summary>
public class RefreshToken : AuditableEntity
{
    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; set; }

    public DateTimeOffset? RevokedAtUtc { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public string? CreatedByIp { get; set; }

    public bool IsActive(DateTimeOffset now) => RevokedAtUtc is null && now < ExpiresAtUtc;
}
