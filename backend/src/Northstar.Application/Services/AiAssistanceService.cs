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
    private readonly IKnowledgeRetrievalService _knowledge;

    public AiAssistanceService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IAiProvider provider)
        : this(db, currentUser, clock, audit, provider, new EmptyKnowledgeRetrievalService())
    {
    }

    public AiAssistanceService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IAiProvider provider,
        IKnowledgeRetrievalService knowledge)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _provider = provider;
        _knowledge = knowledge;
    }

    public async Task<AiSummaryResponse> SummarizeAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);
        var (hits, _) = await RetrieveKnowledgeAsync(context, ct);

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
            Provenance(result.Provider, result.Model, result.IsLiveInference, result.Confidence))
        {
            Sources = hits.Select(ToReference).ToList()
        };
    }

    public async Task<AiSuggestedResponseResponse> SuggestResponseAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);
        var (hits, _) = await RetrieveKnowledgeAsync(context, ct);

        var guidance = SupportGuidanceCatalog.For(ticket.Category)
            .Concat(hits.SelectMany(h => h.ResolutionSteps))
            .Concat(hits.Select(h => h.AgentGuidance))
            .Take(12)
            .ToList();

        var result = await _provider.SuggestResponseAsync(context, guidance, ct);
        await RecordAsync(ticket, AiAssistKind.SuggestedResponse, context, result, null, ct);

        var value = result.Value;
        return new AiSuggestedResponseResponse(
            value.Subject,
            value.Body,
            value.Notes,
            Provenance(result.Provider, result.Model, result.IsLiveInference, null))
        {
            Sources = hits.Select(ToReference).ToList()
        };
    }

    public async Task<AiSentimentResponse> AnalyzeSentimentAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);

        var result = await _provider.AnalyzeSentimentAsync(context, ct);

        var tracked = await _db.Tickets.FirstAsync(t => t.Id == ticket.Id, ct);
        tracked.RecordSentiment(result.Value.Label, _clock.UtcNow);
        await RecordAsync(ticket, AiAssistKind.SentimentAnalysis, context, result, result.Value.Confidence, ct);

        var value = result.Value;
        return new AiSentimentResponse(
            value.Label,
            value.Explanation,
            value.Confidence,
            value.EscalationRecommended,
            value.Signals,
            value.FrustrationIndicators,
            value.UrgencyIndicators,
            Provenance(result.Provider, result.Model, result.IsLiveInference, result.Confidence));
    }

    public async Task<NextBestActionsResponse> NextBestActionsAsync(Guid ticketId, CancellationToken ct)
    {
        var (ticket, context) = await BuildContextAsync(ticketId, ct);
        var (hits, classification) = await RetrieveKnowledgeAsync(context, ct);

        var sentiment = SentimentRules.Analyze(context);
        var lastFromCustomer = context.Conversation.Count > 0 &&
                               context.Conversation[^1].Author == "customer";
        var openAge = (int)Math.Floor((_clock.UtcNow - ticket.CreatedAtUtc).TotalDays);
        var conversationBody = context.Conversation.Select(m => m.Body).ToList();

        var actions = NextBestActionEngine.Recommend(new NextBestActionInput(
            ticket.Category,
            ticket.Status,
            ticket.Priority,
            openAge,
            lastFromCustomer,
            context.Application,
            sentiment.Label,
            context.Subject,
            context.Description,
            conversationBody,
            hits,
            classification));

        var response = new NextBestActionsResponse(
            actions.Select(a => new NextBestActionResponse(
                a.Action,
                a.Reason,
                a.Evidence,
                a.Priority,
                a.RequiresHumanApproval,
                a.ExpectedOutcome,
                a.ActionType)
            {
                Title = a.Title,
                Explanation = a.Explanation,
                Sources = a.Sources.Select(ToReference).ToList(),
                SuggestedWorkflow = a.SuggestedWorkflow,
                Confidence = a.Confidence
            }).ToList(),
            new AiProvenanceResponse(
                "deterministic-rules",
                null,
                IsLiveInference: false,
                RequiresHumanReview: true,
                Disclaimer))
        {
            Classification = new IssueClassificationResponse(
                classification.SuggestedCategory,
                classification.IssueType,
                classification.Confidence,
                classification.Signals,
                classification.Explanation)
        };

        var record = new AiAssistanceRecord
        {
            TicketId = ticket.Id,
            Kind = AiAssistKind.NextBestActions,
            Provider = "deterministic-rules",
            Model = null,
            IsLiveInference = false,
            InputHash = HashContext(context),
            OutputJson = JsonSerializer.Serialize(response),
            Confidence = classification.Confidence,
            LatencyMs = 0,
            CreatedByUserId = RequireUserId(),
            CreatedAtUtc = _clock.UtcNow,
            UpdatedAtUtc = _clock.UtcNow
        };

        _db.AiAssistanceRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "ai.next-best-actions",
            "Ticket",
            ticket.Id.ToString(),
            new
            {
                provider = "deterministic-rules",
                isLiveInference = false,
                classification.SuggestedCategory,
                actionCount = response.Actions.Count
            },
            ct: ct);

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
        // Support agents can read tickets but not applications. Only reviewers and
        // administrators may inject related-application data into AI context or a
        // provider request; everyone else receives ticket-only context.
        if (CanViewRelatedApplication() && ticket.RelatedApplicationId is { } applicationId)
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
            applicationContext,
            Truncate(ticket.Description, 2000));

        return (ticket, context);
    }

    private async Task<(IReadOnlyList<KnowledgeHit> Hits, IssueClassification Classification)>
        RetrieveKnowledgeAsync(AiTicketContext context, CancellationToken ct)
    {
        var classification = IssueClassifier.Classify(context);
        var retrievalCategory = classification.Confidence >= 0.4 &&
                                !classification.SuggestedCategory.Equals("Other", StringComparison.OrdinalIgnoreCase)
            ? classification.SuggestedCategory
            : context.Category;

        var query = new KnowledgeRetrievalQuery(
            retrievalCategory,
            context.Subject,
            context.Description,
            context.Conversation.Select(m => m.Body).ToList(),
            context.Application?.ProductName);

        var hits = await _knowledge.RetrieveAsync(query, 4, ct);
        return (hits, classification);
    }

    private static KnowledgeReferenceResponse ToReference(KnowledgeHit hit) =>
        new(hit.ArticleId, hit.ArticleCode, hit.Title, hit.Category, hit.Score, hit.MatchReason);

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

    private bool CanViewRelatedApplication() =>
        _currentUser.IsInRole(Roles.Reviewer) || _currentUser.IsInRole(Roles.Administrator);

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
