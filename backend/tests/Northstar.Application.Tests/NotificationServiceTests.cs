using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>Covers notification enqueueing, per-user scoping and read bookkeeping.</summary>
public sealed class NotificationServiceTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Enqueueing_creates_a_notification_and_an_outbox_row()
    {
        var user = _fx.SeedUser();
        _fx.Caller.UserId = user.Id;

        _fx.Notifications.EnqueueNotification(
            user.Id, null, NotificationType.General, "Welcome", "Your account is ready.", "test-key-1");
        await _fx.Db.SaveChangesAsync();

        var notification = Assert.Single(await _fx.Db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(user.Id, notification.UserId);
        Assert.Equal(NotificationType.General, notification.Type);
        Assert.False(notification.IsRead);
        Assert.Null(notification.ReadAtUtc);

        var outbox = await _fx.Db.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, outbox.Count);

        var email = Assert.Single(outbox, m => m.Type == "email.notification");
        Assert.Equal("test-key-1", email.IdempotencyKey);
        Assert.Equal(OutboxStatus.Pending, email.Status);

        var externalEvent = Assert.Single(outbox, m => m.Type == "notification.event");
        Assert.Equal("event:test-key-1", externalEvent.IdempotencyKey);
        Assert.Equal(OutboxStatus.Pending, externalEvent.Status);
    }

    [Fact]
    public async Task External_event_payload_contains_only_coarse_safe_fields()
    {
        var user = _fx.SeedUser();
        _fx.Caller.UserId = user.Id;

        _fx.Notifications.EnqueueNotification(
            user.Id, null, NotificationType.General, "Welcome", "Your account is ready.", "safe-key");
        await _fx.Db.SaveChangesAsync();

        var outbox = await _fx.Db.OutboxMessages.AsNoTracking().ToListAsync();
        var externalEvent = Assert.Single(outbox, m => m.Type == "notification.event");

        using var json = JsonDocument.Parse(externalEvent.Payload);
        var root = json.RootElement;

        Assert.Equal("General", root.GetProperty("eventType").GetString());
        Assert.Equal("Notification", root.GetProperty("entityType").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("entityId").ValueKind);
        Assert.Equal(4, root.EnumerateObject().Count());

        Assert.DoesNotContain("Welcome", externalEvent.Payload);
        Assert.DoesNotContain("Your account is ready.", externalEvent.Payload);
        Assert.DoesNotContain(user.Email!, externalEvent.Payload);
        Assert.DoesNotContain(user.Id.ToString(), externalEvent.Payload);
    }

    [Fact]
    public async Task Unread_counts_and_listings_are_scoped_to_the_caller()
    {
        var mine = _fx.SeedUser().Id;
        var other = _fx.SeedUser().Id;

        SeedNotification(mine, isRead: false);
        SeedNotification(mine, isRead: false);
        SeedNotification(mine, isRead: true);
        SeedNotification(other, isRead: false);
        SeedNotification(other, isRead: false);

        _fx.Caller.UserId = mine;
        var service = _fx.Notifications;

        Assert.Equal(2, await service.GetUnreadCountAsync(default));

        var all = await service.ListMineAsync(new PageQuery(1, 10), onlyUnread: false, default);
        Assert.Equal(3, all.TotalCount);
        Assert.Equal(3, all.Items.Count);

        var unreadOnly = await service.ListMineAsync(new PageQuery(1, 10), onlyUnread: true, default);
        Assert.Equal(2, unreadOnly.TotalCount);
    }

    [Fact]
    public async Task Marking_another_users_notification_read_throws_not_found()
    {
        var foreign = SeedNotification(_fx.SeedUser().Id, isRead: false);

        _fx.Caller.UserId = _fx.SeedUser().Id;

        await Assert.ThrowsAsync<NotFoundException>(
            () => _fx.Notifications.MarkReadAsync(foreign.Id, default));

        var stored = await _fx.Db.Notifications.AsNoTracking().SingleAsync(n => n.Id == foreign.Id);
        Assert.False(stored.IsRead);
        Assert.Null(stored.ReadAtUtc);
    }

    [Fact]
    public async Task Marking_all_read_returns_the_number_marked_and_clears_the_unread_count()
    {
        var userId = _fx.SeedUser().Id;
        SeedNotification(userId, isRead: false);
        SeedNotification(userId, isRead: false);
        SeedNotification(userId, isRead: false);

        _fx.Caller.UserId = userId;
        var service = _fx.Notifications;

        Assert.Equal(3, await service.MarkAllReadAsync(default));
        Assert.Equal(0, await service.GetUnreadCountAsync(default));

        // Idempotent: a second sweep marks nothing further.
        Assert.Equal(0, await service.MarkAllReadAsync(default));
        Assert.All(await _fx.Db.Notifications.AsNoTracking().ToListAsync(),
            n => Assert.NotNull(n.ReadAtUtc));
    }

    // ---------------------------------------------------------------- helpers

    private Notification SeedNotification(Guid userId, bool isRead)
    {
        var notification = new Notification
        {
            UserId = userId,
            Type = NotificationType.General,
            Title = "Update",
            Body = "A demonstration notification.",
            IsRead = isRead,
            ReadAtUtc = isRead ? _fx.Clock.UtcNow : null
        };

        _fx.Db.Notifications.Add(notification);
        _fx.Db.SaveChanges();
        return notification;
    }
}
