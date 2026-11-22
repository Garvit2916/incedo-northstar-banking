namespace Northstar.Domain;

/// <summary>
/// Canonical role names. Stored on ASP.NET Core Identity roles and referenced by
/// authorization policies throughout the API.
/// </summary>
public static class Roles
{
    public const string Customer = "Customer";
    public const string SupportAgent = "SupportAgent";
    public const string Reviewer = "Reviewer";
    public const string Administrator = "Administrator";

    public static readonly IReadOnlyList<string> All =
        new[] { Customer, SupportAgent, Reviewer, Administrator };
}

/// <summary>
/// Lifecycle states for a banking product application. The permitted transitions
/// between these states are enforced by <c>ApplicationStateMachine</c>.
/// </summary>
public enum ApplicationStatus
{
    Draft = 0,
    Submitted = 1,
    UnderReview = 2,
    AdditionalInformationRequired = 3,
    Approved = 4,
    Rejected = 5,
    Withdrawn = 6
}

public enum KycDocumentType
{
    ProofOfIdentity = 0,
    ProofOfAddress = 1,
    ProofOfIncome = 2,
    Photograph = 3,
    Other = 4
}

/// <summary>Reviewer decision recorded against an uploaded document.</summary>
public enum DocumentReviewStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    NeedsReplacement = 3
}

/// <summary>
/// Result of the malware/antivirus scanning pipeline. In the development
/// environment scanning is not wired to a real engine and is reported as
/// <see cref="SkippedDev"/> so operators are never misled about coverage.
/// </summary>
public enum ScanStatus
{
    NotScanned = 0,
    Clean = 1,
    Infected = 2,
    ScanFailed = 3,
    SkippedDev = 4
}

public enum TicketStatus
{
    New = 0,
    Open = 1,
    PendingCustomer = 2,
    PendingInternal = 3,
    Escalated = 4,
    Resolved = 5,
    Closed = 6
}

public enum TicketPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum TicketCategory
{
    AccountAccess = 0,
    ApplicationDelay = 1,
    MissingDocuments = 2,
    StatusEnquiry = 3,
    NotificationIssue = 4,
    Other = 5,

    // Banking-specific categories used by the demo support knowledge base and
    // deterministic issue classifier. Appended to preserve persisted values.
    CardTransactionDispute = 6,
    UnauthorizedTransaction = 7,
    CardBlockingReplacement = 8,
    CreditCardBilling = 9,
    FailedUpiTransaction = 10,
    BankTransferIssue = 11,
    KycVerification = 12,
    PersonalLoan = 13,
    FixedDeposit = 14,
    ProductInquiry = 15
}

public enum SentimentLabel
{
    Positive = 0,
    Neutral = 1,
    Negative = 2,
    UrgentEscalation = 3
}

public enum NotificationType
{
    ApplicationSubmitted = 0,
    ApplicationUnderReview = 1,
    AdditionalInformationRequired = 2,
    ApplicationApproved = 3,
    ApplicationRejected = 4,
    DocumentReviewed = 5,
    TicketUpdated = 6,
    General = 7
}

public enum AiAssistKind
{
    Summary = 0,
    SuggestedResponse = 1,
    SentimentAnalysis = 2,
    NextBestActions = 3
}

public enum MessageAuthorType
{
    Customer = 0,
    Agent = 1,
    System = 2
}

public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Failed = 3,
    Abandoned = 4
}
