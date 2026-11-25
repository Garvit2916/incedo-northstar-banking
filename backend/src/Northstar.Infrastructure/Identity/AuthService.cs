using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Infrastructure.Identity;

/// <summary>
/// Authentication use cases built on ASP.NET Core Identity. Failure messages are
/// deliberately generic on the login path to prevent account enumeration, and
/// failed sign-ins are recorded for lockout/abuse detection.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string GenericLoginFailure = "Invalid email address or password.";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IApplicationDbContext _db;
    private readonly TokenService _tokens;
    private readonly IDateTimeProvider _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        IApplicationDbContext db,
        TokenService tokens,
        IDateTimeProvider clock,
        ICurrentUser currentUser,
        IAuditWriter audit,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _db = db;
        _tokens = tokens;
        _clock = clock;
        _currentUser = currentUser;
        _audit = audit;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        await _registerValidator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            return AuthResult.Failure("An account with this email address already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = $"{request.FirstName} {request.LastName}".Trim(),
            IsActive = true,
            CreatedAtUtc = _clock.UtcNow
        };

        var created = await _userManager.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return AuthResult.Failure(created.Errors.Select(e => e.Description).ToArray());
        }

        try
        {
            var roleResult = await _userManager.AddToRoleAsync(user, Roles.Customer);
            if (!roleResult.Succeeded)
            {
                var errors = await RemoveNewUserAsync(user, roleResult.Errors.Select(e => e.Description));
                return AuthResult.Failure(errors.ToArray());
            }
        }
        catch
        {
            // The duplicate-email check above guarantees this is the user created by
            // this call, not a pre-existing account. Preserve the original failure.
            await RemoveNewUserAsync(user, Array.Empty<string>());
            throw;
        }

        _db.CustomerProfiles.Add(new CustomerProfile
        {
            UserId = user.Id,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            PhoneNumber = request.PhoneNumber?.Trim(),
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        });

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("auth.registered", "User", user.Id.ToString(), ct: ct);

        var response = await IssueTokensAsync(user, new[] { Roles.Customer }, ct);
        return AuthResult.Success(response);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        await _loginValidator.ValidateAndThrowAsync(request, ct);

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _userManager.FindByEmailAsync(email);

        if (user is null || !user.IsActive)
        {
            await _audit.RecordAsync("auth.login.failed", "User", null,
                new { email, reason = "unknown-or-inactive" }, succeeded: false, ct: ct);
            return AuthResult.Failure(GenericLoginFailure);
        }

        var valid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!valid)
        {
            await _userManager.AccessFailedAsync(user);
            await _audit.RecordAsync("auth.login.failed", "User", user.Id.ToString(),
                new { reason = "bad-password" }, succeeded: false, ct: ct);
            return AuthResult.Failure(GenericLoginFailure);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            await _audit.RecordAsync("auth.login.locked", "User", user.Id.ToString(),
                null, succeeded: false, ct: ct);
            return AuthResult.Failure("This account is temporarily locked. Please try again later.");
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var response = await IssueTokensAsync(user, roles.ToList(), ct);
        await _audit.RecordAsync("auth.login.succeeded", "User", user.Id.ToString(), ct: ct);

        return AuthResult.Success(response);
    }

    public async Task<AuthResult> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return AuthResult.Failure("A refresh token is required.");
        }

        var hash = TokenService.HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (stored is null)
        {
            return AuthResult.Failure("The refresh token is invalid.");
        }

        var now = _clock.UtcNow;

        if (!stored.IsActive(now))
        {
            // A revoked token being presented is a strong signal of replay.
            // Revoke every outstanding token for the user as a precaution.
            var all = await _db.RefreshTokens
                .Where(t => t.UserId == stored.UserId && t.RevokedAtUtc == null)
                .ToListAsync(ct);

            foreach (var token in all)
            {
                token.RevokedAtUtc = now;
                token.UpdatedAtUtc = now;
            }

            await _db.SaveChangesAsync(ct);
            await _audit.RecordAsync("auth.refresh.reuse-detected", "User", stored.UserId.ToString(),
                null, succeeded: false, ct: ct);

            return AuthResult.Failure("The refresh token is no longer valid. Please sign in again.");
        }

        var user = await _userManager.FindByIdAsync(stored.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            return AuthResult.Failure("The account is not available.");
        }

        var roles = await _userManager.GetRolesAsync(user);
        var response = await IssueTokensAsync(user, roles.ToList(), ct, replacing: stored, now);

        return AuthResult.Success(response);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var hash = TokenService.HashToken(refreshToken);
        var stored = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (stored is null || stored.RevokedAtUtc is not null)
        {
            return;
        }

        stored.RevokedAtUtc = _clock.UtcNow;
        stored.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("auth.logout", "User", stored.UserId.ToString(), ct: ct);
    }

    public async Task<MeResponse?> GetMeAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        return new MeResponse(user.Id, user.Email ?? string.Empty, user.FullName, roles.ToList(), user.IsActive);
    }

    private async Task<IReadOnlyList<string>> RemoveNewUserAsync(
        ApplicationUser user,
        IEnumerable<string> originalErrors)
    {
        var errors = originalErrors.ToList();

        try
        {
            var deleted = await _userManager.DeleteAsync(user);
            if (!deleted.Succeeded)
            {
                var cleanupErrors = deleted.Errors.Select(e => e.Description).ToArray();
                _logger.LogError(
                    "Failed to remove newly registered user {UserId} after role assignment failure: {Errors}",
                    user.Id,
                    string.Join("; ", cleanupErrors));
                errors.Add("Registration cleanup failed; see server logs.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to remove newly registered user {UserId} after role assignment failure.",
                user.Id);
            errors.Add("Registration cleanup failed; see server logs.");
        }

        return errors;
    }

    private async Task<AuthResponse> IssueTokensAsync(
        ApplicationUser user,
        IReadOnlyList<string> roles,
        CancellationToken ct,
        RefreshToken? replacing = null,
        DateTimeOffset? nowOverride = null)
    {
        var now = nowOverride ?? _clock.UtcNow;

        var (accessToken, accessExpires) = _tokens.CreateAccessToken(user, roles);
        var refreshToken = _tokens.CreateRefreshToken();
        var refreshHash = TokenService.HashToken(refreshToken);
        var refreshExpires = _tokens.RefreshTokenExpiry(now);

        if (replacing is not null)
        {
            replacing.RevokedAtUtc = now;
            replacing.ReplacedByTokenHash = refreshHash;
            replacing.UpdatedAtUtc = now;
        }

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            ExpiresAtUtc = refreshExpires,
            CreatedByIp = _currentUser.IpAddress,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        await _db.SaveChangesAsync(ct);

        var summary = new UserSummary(user.Id, user.Email ?? string.Empty, user.FullName, roles, user.IsActive);
        return new AuthResponse(accessToken, accessExpires, refreshToken, refreshExpires, summary);
    }
}
