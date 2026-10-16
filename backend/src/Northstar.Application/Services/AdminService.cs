using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;

namespace Northstar.Application.Services;

/// <summary>Administrative operations: user/role management and audit review.</summary>
public sealed class AdminService
{
    private readonly IApplicationDbContext _db;
    private readonly IUserDirectory _users;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditWriter _audit;

    public AdminService(
        IApplicationDbContext db,
        IUserDirectory users,
        ICurrentUser currentUser,
        IAuditWriter audit)
    {
        _db = db;
        _users = users;
        _currentUser = currentUser;
        _audit = audit;
    }

    public Task<IReadOnlyList<UserSummary>> ListUsersAsync(CancellationToken ct) => _users.ListAsync(ct);

    public async Task AssignRolesAsync(Guid userId, AssignRolesRequest request, CancellationToken ct)
    {
        var roles = request.Roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var invalid = roles.Where(r => !Roles.All.Contains(r, StringComparer.OrdinalIgnoreCase)).ToList();
        if (invalid.Count > 0)
        {
            throw new ConflictException($"Unknown role(s): {string.Join(", ", invalid)}.");
        }

        if (roles.Count == 0)
        {
            throw new ConflictException("At least one role must be assigned.");
        }

        if (!await _users.UserExistsAsync(userId, ct))
        {
            throw new NotFoundException("User was not found.");
        }

        await _users.AssignRolesAsync(userId, roles, ct);
        await _audit.RecordAsync("admin.user.roles.changed", "User", userId.ToString(), new { roles });
    }

    public async Task SetActiveAsync(Guid userId, SetUserActiveRequest request, CancellationToken ct)
    {
        if (_currentUser.UserId == userId && !request.IsActive)
        {
            throw new ConflictException("You cannot deactivate your own account.");
        }

        if (!await _users.SetActiveAsync(userId, request.IsActive, ct))
        {
            throw new NotFoundException("User was not found.");
        }

        await _audit.RecordAsync("admin.user.active.changed", "User", userId.ToString(), new { request.IsActive });
    }

    public async Task<PagedResult<AuditEventResponse>> ListAuditEventsAsync(
        string? action,
        Guid? actorUserId,
        PageQuery page,
        CancellationToken ct)
    {
        var query = _db.AuditEvents.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(action))
        {
            var term = action.Trim();
            query = query.Where(e => e.Action.Contains(term));
        }

        if (actorUserId is not null)
        {
            query = query.Where(e => e.ActorUserId == actorUserId);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.CreatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize)
            .Select(e => new AuditEventResponse(
                e.Id,
                e.ActorUserId,
                e.ActorRole,
                e.Action,
                e.EntityType,
                e.EntityId,
                e.IpAddress,
                e.CorrelationId,
                e.Succeeded,
                e.CreatedAtUtc))
            .ToListAsync(ct);

        return new PagedResult<AuditEventResponse>(items, page.NormalisedPage, page.NormalisedPageSize, total);
    }
}
