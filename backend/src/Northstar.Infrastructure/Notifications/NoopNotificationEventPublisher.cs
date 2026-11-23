using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;

namespace Northstar.Infrastructure.Notifications;

/// <summary>Local/default publisher: intentionally discards external events.</summary>
public sealed class NoopNotificationEventPublisher : INotificationEventPublisher
{
    public Task PublishAsync(NotificationEventPayload notificationEvent, CancellationToken ct) =>
        Task.CompletedTask;
}
