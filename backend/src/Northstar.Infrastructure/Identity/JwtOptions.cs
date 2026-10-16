namespace Northstar.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "northstar";

    public string Audience { get; set; } = "northstar-clients";

    /// <summary>
    /// HMAC signing key. Must be supplied via configuration/user-secrets in
    /// non-development environments and must be at least 32 bytes.
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;
}
