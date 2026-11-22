using Northstar.Application.Abstractions;
using Northstar.Application.Ai;
using Northstar.Application.Contracts;
using Northstar.Domain;

namespace Northstar.Application.Tests;

/// <summary>Clock stub so time-dependent rules are deterministic.</summary>
internal sealed class TestClock : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan by) => UtcNow += by;
}

/// <summary>Ambient caller identity. Defaults to an authenticated caller once a test assigns a user id.</summary>
internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }

    public string? Email { get; set; } = "citizen@northstar.example";

    public bool IsAuthenticated => UserId is not null;

    public HashSet<string> Roles { get; } = new();

    public bool IsInRole(string role) => Roles.Contains(role);

    public string? IpAddress { get; set; } = "127.0.0.1";

    public string? UserAgent { get; set; } = "xunit";

    public string? CorrelationId { get; set; } = "test-correlation-id";
}

/// <summary>Captures audit actions so tests can assert on recorded behaviour.</summary>
internal sealed class TestAuditWriter : IAuditWriter
{
    public List<string> Actions { get; } = new();

    public Task RecordAsync(
        string action,
        string? entityType = null,
        string? entityId = null,
        object? metadata = null,
        bool succeeded = true,
        CancellationToken ct = default)
    {
        Actions.Add(action);
        return Task.CompletedTask;
    }
}

/// <summary>Deterministic reference generator so tests can assert exact values.</summary>
internal sealed class TestReferenceGenerator : IReferenceGenerator
{
    private int _applications;
    private int _tickets;

    public string NextApplicationReference() => $"NS-APP-T{++_applications:D6}";

    public string NextTicketReference() => $"NS-TKT-T{++_tickets:D6}";
}

/// <summary>User directory stub; tests register known agent ids.</summary>
internal sealed class TestUserDirectory : IUserDirectory
{
    public Dictionary<Guid, string> Users { get; } = new();

    public Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UserSummary>>(Array.Empty<UserSummary>());

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Users.ContainsKey(userId));

    public Task<string?> GetEmailAsync(Guid userId, CancellationToken ct) =>
        Task.FromResult(Users.TryGetValue(userId, out var email) ? email : null);

    public Task<bool> AssignRolesAsync(Guid userId, IReadOnlyList<string> roles, CancellationToken ct) =>
        Task.FromResult(true);

    public Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken ct) =>
        Task.FromResult(true);
}

/// <summary>
/// Deterministic AI provider stub. Defaults mirror the development fallback so
/// orchestration, persistence and provenance can be tested without a network.
/// </summary>
internal sealed class TestAiProvider : IAiProvider
{
    public string Name => "test-provider";

    public bool IsLiveInference => false;

    public AiSentiment Sentiment { get; set; } = new(
        SentimentLabel.Negative,
        "Customer is frustrated and still waiting.",
        0.82,
        EscalationRecommended: false,
        Signals: new[] { "frustrated", "still waiting" },
        FrustrationIndicators: new[] { "frustrated" },
        UrgencyIndicators: new[] { "still waiting" });

    public Task<AiCallResult<AiTicketSummary>> SummarizeAsync(AiTicketContext context, CancellationToken ct) =>
        Task.FromResult(new AiCallResult<AiTicketSummary>(
            new AiTicketSummary(
                "Customer is waiting for an update.",
                new[] { "Previous message unanswered" },
                new[] { "Ticket reviewed" },
                new[] { "Send a status update" },
                "Open and awaiting agent reply",
                Array.Empty<string>()),
            Name,
            "test-model",
            IsLiveInference,
            null,
            12));

    public Task<AiCallResult<AiSuggestedResponse>> SuggestResponseAsync(
        AiTicketContext context,
        IReadOnlyList<string> guidance,
        CancellationToken ct) =>
        Task.FromResult(new AiCallResult<AiSuggestedResponse>(
            new AiSuggestedResponse(
                "Update on your request",
                "Thank you for your patience. We are reviewing your request.",
                true,
                Array.Empty<string>()),
            Name,
            "test-model",
            IsLiveInference,
            null,
            10));

    public Task<AiCallResult<AiSentiment>> AnalyzeSentimentAsync(
        AiTicketContext context,
        CancellationToken ct) =>
        Task.FromResult(new AiCallResult<AiSentiment>(
            Sentiment,
            Name,
            "test-model",
            IsLiveInference,
            Sentiment.Confidence,
            8));
}

/// <summary>Captures retrieval queries and returns test-controlled knowledge hits.</summary>
internal sealed class TestKnowledgeRetrieval : IKnowledgeRetrievalService
{
    public IReadOnlyList<KnowledgeHit> Hits { get; set; } = Array.Empty<KnowledgeHit>();

    public KnowledgeRetrievalQuery? LastQuery { get; private set; }

    public int LastMaxResults { get; private set; }

    public Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        KnowledgeRetrievalQuery query,
        int maxResults = 4,
        CancellationToken ct = default)
    {
        LastQuery = query;
        LastMaxResults = maxResults;
        return Task.FromResult(Hits);
    }
}
