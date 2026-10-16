using Northstar.Domain;

namespace Northstar.Application.Contracts;

public sealed record NotificationResponse(
    Guid Id,
    NotificationType Type,
    string Title,
    string Body,
    bool IsRead,
    Guid? ApplicationId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ReadAtUtc);
