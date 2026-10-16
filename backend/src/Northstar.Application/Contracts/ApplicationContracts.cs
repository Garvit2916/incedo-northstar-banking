using Northstar.Domain;

namespace Northstar.Application.Contracts;

public sealed record CreateApplicationRequest(
    Guid ProductId,
    Dictionary<string, string>? Answers);

public sealed record UpdateApplicationDraftRequest(Dictionary<string, string> Answers);

public sealed record ReasonRequest(string Reason);

public sealed record ApplicationSummaryResponse(
    Guid Id,
    string Reference,
    Guid ProductId,
    string ProductName,
    ApplicationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc);

public sealed record ApplicationStatusHistoryResponse(
    ApplicationStatus? FromStatus,
    ApplicationStatus ToStatus,
    Guid? ChangedByUserId,
    string? Reason,
    DateTimeOffset ChangedAtUtc);

public sealed record ApplicationDetailResponse(
    Guid Id,
    string Reference,
    Guid ProductId,
    string ProductName,
    ApplicationStatus Status,
    IReadOnlyDictionary<string, string> Answers,
    string? DecisionReason,
    Guid? AssignedReviewerId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? SubmittedAtUtc,
    DateTimeOffset? DecidedAtUtc,
    IReadOnlyList<ApplicationStatusHistoryResponse> History,
    IReadOnlyList<DocumentResponse> Documents);

public sealed record DocumentResponse(
    Guid Id,
    Guid ApplicationId,
    KycDocumentType DocumentType,
    string OriginalFileName,
    string ContentType,
    long SizeBytes,
    ScanStatus ScanStatus,
    DocumentReviewStatus ReviewStatus,
    string? ReviewNotes,
    DateTimeOffset UploadedAtUtc,
    DateTimeOffset? ReviewedAtUtc,
    bool IsReviewable);

public sealed record DocumentReviewRequest(DocumentReviewStatus Decision, string? Notes);

public sealed record DocumentUpload(
    string OriginalFileName,
    string ContentType,
    long Length,
    Stream Content,
    KycDocumentType DocumentType);

public sealed record DownloadLinkResponse(string Url, string FileName, DateTimeOffset ExpiresAtUtc);

/// <summary>Authorised document content streamed through the API when no scoped storage URL is available.</summary>
public sealed record DocumentContent(Stream Content, string FileName, string ContentType);
