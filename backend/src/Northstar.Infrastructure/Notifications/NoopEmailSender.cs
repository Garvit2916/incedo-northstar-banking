using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Notifications;

/// <summary>
/// Development email transport. Messages are logged as metadata only (recipient
/// and subject) and never actually sent, so the application can be demonstrated
/// without a configured provider. Message bodies are not logged to avoid
/// retaining customer content.
/// </summary>
public sealed class NoopEmailSender : IEmailSender
{
    private readonly ILogger<NoopEmailSender> _logger;

    public NoopEmailSender(ILogger<NoopEmailSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        _logger.LogInformation(
            "[NoopEmail] Would send email to {Recipient} with subject {Subject}. " +
            "Configure Email:Provider to enable real delivery.",
            MaskEmail(message.To),
            message.Subject);

        return Task.CompletedTask;
    }

    private static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        if (at <= 1)
        {
            return "***";
        }

        return $"{email[0]}***{email[at..]}";
    }
}
