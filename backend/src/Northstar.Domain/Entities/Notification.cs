using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>In-app notification delivered to a specific user.</summary>
public class Notification : AuditableEntity
{
    public Guid UserId { get; set; }

    public Guid? ApplicationId { get; set; }

    public NotificationType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool IsRead { get; set; }

    public DateTimeOffset? ReadAtUtc { get; set; }

    public void MarkRead(DateTimeOffset now)
    {
        if (IsRead)
        {
            return;
        }

        IsRead = true;
        ReadAtUtc = now;
    }
}
