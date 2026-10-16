using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;

namespace Northstar.Application.Services;

/// <summary>Aggregated, role-appropriate dashboard figures.</summary>
public sealed class DashboardService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;

    public DashboardService(IApplicationDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<DashboardStatsResponse> GetCustomerStatsAsync(CancellationToken ct)
    {
        var userId = RequireUserId();

        var applications = _db.Applications
            .AsNoTracking()
            .Where(a => a.CustomerProfile!.UserId == userId);

        var openTickets = await _db.Tickets.CountAsync(t => t.CustomerUserId == userId &&
            t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed, ct);

        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return await BuildAsync(applications, openTickets, unread, ct);
    }

    public async Task<DashboardStatsResponse> GetOperationsStatsAsync(CancellationToken ct)
    {
        var userId = RequireUserId();

        var openTickets = await _db.Tickets.CountAsync(t =>
            t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed, ct);

        var unread = await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);

        return await BuildAsync(_db.Applications.AsNoTracking(), openTickets, unread, ct);
    }

    private static async Task<DashboardStatsResponse> BuildAsync(
        IQueryable<Domain.Entities.Application> applications,
        int openTickets,
        int unread,
        CancellationToken ct)
    {
        var byStatus = await applications
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int Count(ApplicationStatus status) => byStatus.FirstOrDefault(x => x.Status == status)?.Count ?? 0;

        return new DashboardStatsResponse(
            Count(ApplicationStatus.Draft),
            Count(ApplicationStatus.Submitted),
            Count(ApplicationStatus.UnderReview),
            Count(ApplicationStatus.Approved),
            Count(ApplicationStatus.Rejected),
            openTickets,
            unread);
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
