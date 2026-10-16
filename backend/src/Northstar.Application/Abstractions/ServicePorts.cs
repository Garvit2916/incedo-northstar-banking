using Northstar.Application.Contracts;

namespace Northstar.Application.Abstractions;

/// <summary>
/// Authentication use cases. Implemented in infrastructure because it is built on
/// ASP.NET Core Identity, but expressed here in terms of application contracts so
/// the API layer depends only on this abstraction.
/// </summary>
public interface IAuthService
{
    Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct);

    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct);

    Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct);

    Task LogoutAsync(string refreshToken, CancellationToken ct);

    Task<MeResponse?> GetMeAsync(CancellationToken ct);
}

/// <summary>Administrative access to the user/role store.</summary>
public interface IUserDirectory
{
    Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct);

    Task<bool> UserExistsAsync(Guid userId, CancellationToken ct);

    Task<string?> GetEmailAsync(Guid userId, CancellationToken ct);

    Task<bool> AssignRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct);

    Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct);
}

/// <summary>Generates human-friendly unique references for applications and tickets.</summary>
public interface IReferenceGenerator
{
    string NextApplicationReference();

    string NextTicketReference();
}

/// <summary>
/// Payload stored in the transactional outbox for asynchronous email delivery.
/// The recipient is referenced by user id; the address is resolved at send time
/// so the outbox never stores an email address longer than it must.
/// </summary>
public sealed record EmailNotificationPayload(Guid UserId, string Subject, string Body);
