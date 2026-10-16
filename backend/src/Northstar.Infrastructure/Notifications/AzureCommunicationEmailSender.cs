using Azure;
using Azure.Communication.Email;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using AppEmailMessage = Northstar.Application.Abstractions.EmailMessage;

namespace Northstar.Infrastructure.Notifications;

/// <summary>Azure Communication Services email transport for cloud deployments.</summary>
public sealed class AzureCommunicationEmailSender : IEmailSender
{
    private readonly EmailClient _client;
    private readonly EmailOptions _options;

    public AzureCommunicationEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
        _client = new EmailClient(_options.AzureCommunicationConnectionString);
    }

    public async Task SendAsync(AppEmailMessage message, CancellationToken ct)
    {
        var content = new EmailContent(message.Subject)
        {
            PlainText = message.IsHtml ? null : message.Body,
            Html = message.IsHtml ? message.Body : null
        };

        var recipients = new EmailRecipients(new[] { new EmailAddress(message.To) });
        var emailMessage = new Azure.Communication.Email.EmailMessage(_options.FromAddress, recipients, content);

        await _client.SendAsync(WaitUntil.Started, emailMessage, ct);
    }
}
