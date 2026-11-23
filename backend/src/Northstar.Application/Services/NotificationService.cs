using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// In-app notifications and the transactional outbox that drives email and
/// coarse external event delivery.
///
/// Notifications are always written in the same unit of work as the business
/// change they describe. Email and SNS delivery are decoupled through the outbox
/// so that a provider outage can never roll back a valid decision, and the
/// idempotency keys prevent duplicate delivery on retry.
/// </summary>
public sealed class NotificationService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public NotificationService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<PagedResult<NotificationResponse>> ListMineAsync(
        PageQuery page,
        bool onlyUnread,
        CancellationToken ct)
    {
        var userId = RequireUserId();

        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId);
        if (onlyUnread)
        {
            query = query.Where(n => !n.IsRead);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize)
            .ToListAsync(ct);

        return new PagedResult<NotificationResponse>(
            items.Select(n => n.ToResponse()).ToList(),
            page.NormalisedPage,
            page.NormalisedPageSize,
            total);
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken ct)
    {
        var userId = RequireUserId();
        return await _db.Notifications.CountAsync(n => n.UserId == userId && !n.IsRead, ct);
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var notification = await _db.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, ct)
            ?? throw new NotFoundException("Notification was not found.");

        notification.MarkRead(_clock.UtcNow);
        notification.UpdatedAtUtc = _clock.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    public async Task<int> MarkAllReadAsync(CancellationToken ct)
    {
        var userId = RequireUserId();
        var unread = await _db.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ToListAsync(ct);

        foreach (var notification in unread)
        {
            notification.MarkRead(_clock.UtcNow);
            notification.UpdatedAtUtc = _clock.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return unread.Count;
    }

    /// <summary>
    /// Creates an in-app notification and enqueues an email through the outbox.
    /// Callers must persist this within the same SaveChanges as the business change.
    /// </summary>
    public void EnqueueNotification(
        Guid userId,
        Guid? applicationId,
        NotificationType type,
        string title,
        string body,
        string idempotencyKey)
    {
        var now = _clock.UtcNow;

        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            ApplicationId = applicationId,
            Type = type,
            Title = title,
            Body = body,
            IsRead = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        var emailPayload = JsonSerializer.Serialize(new EmailNotificationPayload(userId, title, body));
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Type = "email.notification",
            Payload = emailPayload,
            IdempotencyKey = idempotencyKey,
            Status = OutboxStatus.Pending,
            OccurredAtUtc = now
        });

        // A separate outbox row keeps SNS publication transactional with the
        // business change and independent of email delivery. It intentionally
        // carries only a coarse event type and internal entity reference.
        var eventPayload = JsonSerializer.Serialize(new NotificationEventPayload(
            type.ToString(),
            "Notification",
            null,
            now));
        _db.OutboxMessages.Add(new OutboxMessage
        {
            Type = "notification.event",
            Payload = eventPayload,
            IdempotencyKey = $"event:{idempotencyKey}",
            Status = OutboxStatus.Pending,
            OccurredAtUtc = now
        });
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
