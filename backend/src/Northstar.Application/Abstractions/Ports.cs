using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Northstar.Application.Ai;
using Northstar.Domain.Entities;

namespace Northstar.Application.Abstractions;

/// <summary>
/// Database surface required by application services. Defined in the application
/// layer so use cases remain testable and expressed in terms of the domain, while
/// the concrete EF Core context lives in infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<CustomerProfile> CustomerProfiles { get; }
    DbSet<Product> Products { get; }
    DbSet<Domain.Entities.Application> Applications { get; }
    DbSet<ApplicationStatusHistory> ApplicationStatusHistory { get; }
    DbSet<KycDocument> KycDocuments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<OutboxMessage> OutboxMessages { get; }
    DbSet<Ticket> Tickets { get; }
    DbSet<TicketMessage> TicketMessages { get; }
    DbSet<TicketAssignment> TicketAssignments { get; }
    DbSet<AiAssistanceRecord> AiAssistanceRecords { get; }
    DbSet<KnowledgeArticle> KnowledgeArticles { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);
}

/// <summary>Ambient identity of the caller for the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);
    string? IpAddress { get; }
    string? UserAgent { get; }
    string? CorrelationId { get; }
}

/// <summary>Injectable clock so time-dependent rules are deterministically testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }
}

public sealed record StoredBlob(string StorageKey, long SizeBytes, string Sha256);

/// <summary>
/// Private object storage abstraction. Local disk in development, Azure Blob
/// Storage in the cloud. Implementations must never expose a public URL.
/// </summary>
public interface IBlobStorage
{
    Task<StoredBlob> SaveAsync(Stream content, string storageKey, string contentType, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);

    Task DeleteAsync(string storageKey, CancellationToken ct);

    /// <summary>
    /// Returns a short-lived, scoped access URL when the backing store supports it
    /// (e.g. an Azure Blob SAS), otherwise null so the API can stream the content
    /// through an authorised endpoint instead.
    /// </summary>
    Task<Uri?> GetPresignedUrlAsync(string storageKey, TimeSpan ttl, CancellationToken ct);
}

/// <summary>
/// Malware scanning integration point. Implementations return the scan verdict
/// for uploaded content before it is made available to reviewers.
/// </summary>
public interface IFileScanner
{
    Task<Domain.ScanStatus> ScanAsync(Stream content, string contentType, CancellationToken ct);
}

public sealed record EmailMessage(string To, string Subject, string Body, bool IsHtml = false);

/// <summary>Notification transport. A no-op implementation is used in development.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

/// <summary>Writes append-only audit events for security- and business-critical actions.</summary>
public interface IAuditWriter
{
    Task RecordAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        bool succeeded = true,
        CancellationToken ct = default);
}

/// <summary>
/// AI provider abstraction. Four capabilities are required by the assessment:
/// summarisation, response drafting, sentiment classification, and (assistive)
/// next-best-action narration. Next-best actions themselves are produced by the
/// deterministic <c>NextBestActionEngine</c>; the provider only enriches language.
/// </summary>
public interface IAiProvider
{
    string Name { get; }

    /// <summary>True only when a configured live model produced the response.</summary>
    bool IsLiveInference { get; }

    Task<AiCallResult<AiTicketSummary>> SummarizeAsync(AiTicketContext context, CancellationToken ct);

    Task<AiCallResult<AiSuggestedResponse>> SuggestResponseAsync(
        AiTicketContext context,
        IReadOnlyList<string> guidance,
        CancellationToken ct);

    Task<AiCallResult<AiSentiment>> AnalyzeSentimentAsync(AiTicketContext context, CancellationToken ct);
}

/// <summary>
/// Retrieves approved demo knowledge articles for a ticket. Deterministic and
/// database-backed; a vector implementation can replace this adapter later
/// without changing AI orchestration or the support workspace contracts.
/// </summary>
public interface IKnowledgeRetrievalService
{
    Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        KnowledgeRetrievalQuery query,
        int maxResults = 4,
        CancellationToken ct = default);
}
