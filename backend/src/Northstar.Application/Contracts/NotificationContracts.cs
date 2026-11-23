using System.Text.Json.Serialization;
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

/// <summary>
/// Deliberately minimal event payload. It contains no email address, customer
/// name, notification body or KYC/application detail.
/// </summary>
public sealed record NotificationEventPayload(
    [property: JsonPropertyName("eventType")] string EventType,
    [property: JsonPropertyName("entityType")] string EntityType,
    [property: JsonPropertyName("entityId")] string? EntityId,
    [property: JsonPropertyName("occurredAtUtc")] DateTimeOffset OccurredAtUtc);
