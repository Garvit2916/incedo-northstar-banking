namespace Northstar.Infrastructure.Notifications;

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    /// <summary>"Noop" (development), "Smtp", or "AzureCommunication".</summary>
    public string Provider { get; set; } = "Noop";

    public string FromAddress { get; set; } = "no-reply@northstar.example";

    public string FromName { get; set; } = "Northstar Banking";

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public string? SmtpUser { get; set; }

    public string? SmtpPassword { get; set; }

    public bool SmtpUseStartTls { get; set; } = true;

    public string? AzureCommunicationConnectionString { get; set; }
}
