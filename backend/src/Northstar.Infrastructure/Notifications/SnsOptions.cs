namespace Northstar.Infrastructure.Notifications;

public sealed class SnsOptions
{
    public const string SectionName = "Sns";

    /// <summary>
    /// Topic ARN used for coarse notification events. When empty, the no-op
    /// publisher is registered so local development needs no AWS configuration.
    /// </summary>
    public string TopicArn { get; set; } = string.Empty;

    /// <summary>Optional region override; ECS normally supplies AWS_REGION.</summary>
    public string Region { get; set; } = string.Empty;
}
