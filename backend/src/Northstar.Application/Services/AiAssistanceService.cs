using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Ai;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Orchestrates AI assistance for tickets: builds a minimised context, invokes the
/// configured provider, records operational metadata, and returns results clearly
/// labelled with their provenance and a human-review requirement.
///
/// Safety posture:
/// - ticket text is treated as untrusted data (never as instructions);
/// - inputs are length-capped and messages truncated;
/// - output is a strongly-typed contract, not free-form text;
/// - next-best actions come from deterministic rules, never the model;
/// - nothing here can approve an application or alter permissions.
/// </summary>
public sealed class AiAssistanceService
{
    private const int MaxMessages = 20;
    private const int MaxMessageLength = 2000;
    private const string Disclaimer =
        "AI-generated content is assistive only. Review and edit it before use. " +
        "It cannot approve applications, change permissions, or perform privileged actions.";

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IAiProvider _provider;

    public AiAssistanceService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IAiProvider provider)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _provider = provider;
    }

    public async Task<AiSummaryResponse> SummarizeAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);

        var result = await _provider.SummarizeAsync(context, ct);
        await RecordAsync(ticket, AiAssistKind.Summary, context, result, null, ct);

        var value = result.Value;
        return new AiSummaryResponse(
            value.MainIssue,
            value.KeyContext,
            value.ActionsTaken,
            value.OutstandingItems,
            value.CurrentState,
            value.Uncertainties,
            Provenance(result.Provider, result.Model, result.IsLiveInference, result.Confidence));
    }

    public async Task<AiSuggestedResponseResponse> SuggestResponseAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);
        var guidance = SupportGuidanceCatalog.For(ticket.Category);

        var result = await _provider.SuggestResponseAsync(context, guidance, ct);
        await RecordAsync(ticket, AiAssistKind.SuggestedResponse, context, result, null, ct);

        var value = result.Value;
        return new AiSuggestedResponseResponse(
            value.Subject,
            value.Body,
            value.Notes,
            Provenance(result.Provider, result.Model, result.IsLiveInference, null));
    }

    public async Task<AiSentimentResponse> AnalyzeSentimentAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);

        var result = await _provider.AnalyzeSentimentAsync(context, ct);
        await RecordAsync(ticket, AiAssistKind.SentimentAnalysis, context, result, result.Value.Confidence, ct);

        var value = result.Value;
        return new AiSentimentResponse(
            value.Label,
            value.Explanation,
            value.Confidence,
            value.EscalationRecommended,
            value.Signals,
            Provenance(result.Provider, result.Model, result.IsLiveInference, result.Confidence));
    }

    public async Task<NextBestActionsResponse> NextBestActionsAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);

        var sentiment = SentimentRules.Analyze(context);
        var lastFromCustomer = context.Conversation.Count > 0 &&
                               context.Conversation[^1].Author == "customer";

        var openAge = (int)Math.Floor((_clock.UtcNow - ticket.CreatedAtUtc).TotalDays);

        var actions = NextBestActionEngine.Recommend(new NextBestActionInput(
            ticket.Category,
            ticket.Status,
            ticket.Priority,
            openAge,
            lastFromCustomer,
            context.Application,
            sentiment.Label));

        var response = new NextBestActionsResponse(
            actions.Select(a => new NextBestActionResponse(
                a.Action, a.Reason, a.Evidence, a.Priority, a.RequiresHumanApproval)).ToList(),
            new AiProvenanceResponse(
                "deterministic-rules",
                null,
                IsLiveInference: false,
                RequiresHumanReview: true,
                Disclaimer));

        var record = new AiAssistanceRecord
        {
            TicketId = ticket.Id,
            Kind = AiAssistKind.NextBestActions,
            Provider = "deterministic-rules",
            Model = null,
            IsLiveInference = false,
            InputHash = HashContext(context),
            OutputJson = JsonSerializer.Serialize(response.Actions),
            Confidence = null,
            LatencyMs = 0,
            CreatedByUserId = RequireUserId(),
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };

        _db.AiAssistanceRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        return response;
    }

    // ------------------------------------------------------------------ helpers

    private async Task<(Ticket Ticket, AiTicketContext Context)> BuildContextAsync(Guid ticketId, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == ticketId, ct)
            ?? throw new NotFoundException("Ticket was not found.");

        AiApplicationContext? applicationContext = null;
        if (ticket.RelatedApplicationId is { } applicationId)
        {
            var application = await _db.Applications
                .AsNoTracking()
                .Include(a => a.Product)
                .Include(a => a.Documents)
                .FirstOrDefaultAsync(a => a.Id == applicationId, ct);

            if (application is not null)
            {
                var documentStates = application.Documents
                    .Select(d => $"{d.DocumentType}={d.ReviewStatus}")
                    .ToList();

                var hasOutstanding = application.Documents.Count == 0 ||
                    application.Documents.Any(d => d.ReviewStatus != DocumentReviewStatus.Approved);

                applicationContext = new AiApplicationContext(
                    application.Reference,
                    application.Product?.Name ?? "Unknown product",
                    application.Status.ToString(),
                    documentStates,
                    hasOutstanding);
            }
        }

        var conversation = ticket.Messages
            .Where(m => !m.IsInternalNote)
            .OrderBy(m => m.CreatedAtUtc)
            .TakeLast(MaxMessages)
            .Select(m => new AiConversationMessage(AuthorName(m.AuthorType), Truncate(m.Body, MaxMessageLength)))
            .ToList();

        var context = new AiTicketContext(
            ticket.Reference,
            Truncate(ticket.Subject, 300),
            ticket.Category.ToString(),
            ticket.Priority.ToString(),
            ticket.Status.ToString(),
            conversation,
            applicationContext);

        return (ticket, context);
    }

    private async Task RecordAsync<T>(
        Ticket ticket,
        AiAssistKind kind,
        AiTicketContext context,
        AiCallResult<T> result,
        double? confidence,
        CancellationToken ct)
    {
        var record = new AiAssistanceRecord
        {
            TicketId = ticket.Id,
            Kind = kind,
            Provider = result.Provider,
            Model = result.Model,
            IsLiveInference = result.IsLiveInference,
            InputHash = HashContext(context),
            OutputJson = JsonSerializer.Serialize(result.Value),
            Confidence = confidence,
            LatencyMs = result.LatencyMs,
            CreatedByUserId = RequireUserId(),
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };

        _db.AiAssistanceRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync($"ai.{kind.ToString().ToLowerInvariant()}", "Ticket", ticket.Id.ToString(), new
        {
            result.Provider,
            result.IsLiveInference
        });
    }

    private static string AuthorName(MessageAuthorType type) => type switch
    {
        MessageAuthorType.Customer => "customer",
        MessageAuthorType.Agent => "agent",
        _ => "system"
    };

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value[..Math.Min(value.Length, max)];

    private static string HashContext(AiTicketContext context)
    {
        var json = JsonSerializer.Serialize(context);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static AiProvenanceResponse Provenance(string provider, string? model, bool isLive, double? confidence) =>
        new(provider, model, isLive, RequiresHumanReview: true, Disclaimer);

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
