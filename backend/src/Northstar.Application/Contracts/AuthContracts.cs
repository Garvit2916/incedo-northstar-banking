namespace Northstar.Application.Contracts;

public sealed record RegisterRequest(
    string Email,
    string Password,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    string? ConfirmPassword = null);

public sealed record LoginRequest(string Email, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record UserSummary(Guid Id, string Email, string FullName, IReadOnlyList<string> Roles, bool IsActive);

public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserSummary User);

public sealed record MeResponse(
    Guid Id,
    string Email,
    string FullName,
    IReadOnlyList<string> Roles,
    bool IsActive);

/// <summary>
/// Result of an authentication attempt. <see cref="Errors"/> is populated only
/// for validation-type failures and never distinguishes between unknown accounts
/// and incorrect passwords on the login path.
/// </summary>
public sealed record AuthResult(
    bool Succeeded,
    AuthResponse? Response = null,
    IReadOnlyList<string>? Errors = null)
{
    public static AuthResult Success(AuthResponse response) => new(true, response);

    public static AuthResult Failure(params string[] errors) => new(false, null, errors);
}
