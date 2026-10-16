using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Northstar.Application.Abstractions;

namespace Northstar.Infrastructure.Notifications;

/// <summary>SMTP transport used when a real mail relay is configured.</summary>
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;

    public SmtpEmailSender(IOptions<EmailOptions> options)
    {
        _options = options.Value;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var host = _options.SmtpHost;
        if (string.IsNullOrWhiteSpace(host))
        {
            throw new InvalidOperationException(
                "Email:Provider is 'Smtp' but Email:SmtpHost is not configured.");
        }

        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_options.FromName, _options.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new TextPart(message.IsHtml ? "html" : "plain") { Text = message.Body };

        using var client = new SmtpClient();
        await client.ConnectAsync(
            host,
            _options.SmtpPort,
            _options.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
            ct);

        if (!string.IsNullOrWhiteSpace(_options.SmtpUser))
        {
            await client.AuthenticateAsync(_options.SmtpUser, _options.SmtpPassword ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(true, ct);
    }
}
