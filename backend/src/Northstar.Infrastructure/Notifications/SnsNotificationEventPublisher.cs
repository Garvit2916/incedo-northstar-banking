using System.Text.Json;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;

namespace Northstar.Infrastructure.Notifications;

/// <summary>
/// Publishes coarse notification events to SNS using the ECS task role. The
/// payload is restricted to <see cref="NotificationEventPayload"/>, which carries
/// no customer, account, KYC or email information.
/// </summary>
public sealed class SnsNotificationEventPublisher : INotificationEventPublisher
{
    private readonly IAmazonSimpleNotificationService _client;
    private readonly string _topicArn;
    private readonly ILogger<SnsNotificationEventPublisher> _logger;

    public SnsNotificationEventPublisher(
        IAmazonSimpleNotificationService client,
        string topicArn,
        ILogger<SnsNotificationEventPublisher> logger)
    {
        if (string.IsNullOrWhiteSpace(topicArn))
        {
            throw new InvalidOperationException(
                "Sns:TopicArn must be configured when SNS publishing is enabled.");
        }

        _client = client ?? throw new ArgumentNullException(nameof(client));
        _topicArn = topicArn;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task PublishAsync(NotificationEventPayload notificationEvent, CancellationToken ct)
    {
        var request = new PublishRequest
        {
            TopicArn = _topicArn,
            Message = JsonSerializer.Serialize(notificationEvent),
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                ["eventType"] = new()
                {
                    DataType = "String",
                    StringValue = notificationEvent.EventType
                }
            }
        };

        await _client.PublishAsync(request, ct);
        _logger.LogInformation("Published notification event {EventType} to SNS.", notificationEvent.EventType);
    }
}
