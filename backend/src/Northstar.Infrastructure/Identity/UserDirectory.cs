using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;
using Northstar.Infrastructure.Persistence;

namespace Northstar.Infrastructure.Identity;

/// <summary>Read/write access to the Identity user and role store for administrators.</summary>
public sealed class UserDirectory : IUserDirectory
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public UserDirectory(UserManager<ApplicationUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct)
    {
        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive })
            .ToListAsync(ct);

        var roleRows = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            select new { userRole.UserId, Name = role.Name })
            .ToListAsync(ct);

        var rolesByUser = roleRows
            .GroupBy(r => r.UserId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name!).ToList());

        return users
            .Select(u => new UserSummary(
                u.Id,
                u.Email ?? string.Empty,
                u.FullName,
                rolesByUser.TryGetValue(u.Id, out var roles) ? roles : Array.Empty<string>(),
                u.IsActive))
            .ToList();
    }

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) =>
        _db.Users.AnyAsync(u => u.Id == userId, ct);

    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken ct) =>
        await _db.Users.Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync(ct);

    public async Task<bool> AssignRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var current = await _userManager.GetRolesAsync(user);
        var toRemove = current.Except(roles, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = roles.Except(current, StringComparer.OrdinalIgnoreCase).ToList();

        if (toRemove.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, toRemove);
        }

        if (toAdd.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, toAdd);
        }

        return true;
    }

    public async Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        user.IsActive = isActive;
        await _userManager.UpdateAsync(user);
        return true;
    }
}
