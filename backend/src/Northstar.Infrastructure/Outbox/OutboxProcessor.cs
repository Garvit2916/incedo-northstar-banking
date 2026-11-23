using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Northstar.Application.Abstractions;
using Northstar.Application.Contracts;
using Northstar.Domain;

namespace Northstar.Infrastructure.Outbox;

/// <summary>
/// Drains the transactional outbox and dispatches email notifications and SNS
/// notification events through separate message types.
///
/// Delivery is at-least-once: on success a message is marked Sent and removed
/// from the pending set. Failures are retried with exponential back-off and a
/// bounded attempt count. A delivery failure never affects the business decision
/// that produced the notification, which was committed transactionally already.
/// </summary>
public sealed class OutboxProcessor : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private const int BatchSize = 20;
    private const int MaxAttempts = 5;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(IServiceScopeFactory scopeFactory, ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox processing cycle failed.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var email = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var users = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var clock = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var events = scope.ServiceProvider.GetRequiredService<INotificationEventPublisher>();

        var now = clock.UtcNow;

        // The status filter is applied in SQL; the due-time and ordering predicates are
        // evaluated in memory because the SQLite provider cannot translate nullable
        // DateTimeOffset comparisons. The pending set is small by design.
        var candidates = await db.OutboxMessages
            .Where(m => m.Status == OutboxStatus.Pending)
            .ToListAsync(ct);

        var pending = candidates
            .Where(m => m.NextAttemptAtUtc is null || m.NextAttemptAtUtc <= now)
            .OrderBy(m => m.OccurredAtUtc)
            .Take(BatchSize)
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        foreach (var message in pending)
        {
            message.Status = OutboxStatus.Processing;

            try
            {
                if (message.Type == "notification.event")
                {
                    var payload = JsonSerializer.Deserialize<NotificationEventPayload>(message.Payload)
                                  ?? throw new InvalidOperationException("Outbox event payload could not be parsed.");

                    await events.PublishAsync(payload, ct);
                }
                else
                {
                    var payload = JsonSerializer.Deserialize<EmailNotificationPayload>(message.Payload)
                                  ?? throw new InvalidOperationException("Outbox payload could not be parsed.");

                    var address = await users.GetEmailAsync(payload.UserId, ct);
                    if (string.IsNullOrWhiteSpace(address))
                    {
                        message.Status = OutboxStatus.Abandoned;
                        message.LastError = "Recipient address could not be resolved.";
                        message.ProcessedAtUtc = now;
                        continue;
                    }

                    await email.SendAsync(new EmailMessage(address, payload.Subject, payload.Body), ct);
                }

                message.Status = OutboxStatus.Sent;
                message.ProcessedAtUtc = now;
                message.LastError = null;
            }
            catch (Exception ex)
            {
                message.Attempts++;
                message.LastError = Truncate(ex.Message, 1000);

                if (message.Attempts >= MaxAttempts)
                {
                    message.Status = OutboxStatus.Abandoned;
                    message.ProcessedAtUtc = now;
                    _logger.LogError(
                        "Outbox message {Id} abandoned after {Attempts} attempts.",
                        message.Id, message.Attempts);
                }
                else
                {
                    message.Status = OutboxStatus.Pending;
                    var backoff = TimeSpan.FromSeconds(Math.Pow(2, message.Attempts) * 15);
                    message.NextAttemptAtUtc = now.Add(backoff);
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
