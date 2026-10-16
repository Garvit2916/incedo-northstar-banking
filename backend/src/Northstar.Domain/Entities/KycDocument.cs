using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Metadata for a KYC document stored in private object storage. The binary
/// content lives outside the database under <see cref="StorageKey"/>, which is a
/// server-generated identifier - never the customer-supplied filename.
/// </summary>
public class KycDocument : AuditableEntity
{
    public Guid ApplicationId { get; set; }

    public Application Application { get; set; } = null!;

    public Guid CustomerProfileId { get; set; }

    public CustomerProfile CustomerProfile { get; set; } = null!;

    public KycDocumentType DocumentType { get; set; }

    /// <summary>Original filename, retained for display only and never used as a path.</summary>
    public string OriginalFileName { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public ScanStatus ScanStatus { get; set; } = ScanStatus.NotScanned;

    public DocumentReviewStatus ReviewStatus { get; set; } = DocumentReviewStatus.Pending;

    public string? ReviewNotes { get; set; }

    public DateTimeOffset UploadedAtUtc { get; set; }

    public Guid? ReviewedByUserId { get; set; }

    public DateTimeOffset? ReviewedAtUtc { get; set; }

    /// <summary>
    /// A document is available to reviewers only after a completed scan. In the
    /// development environment scanning is skipped and explicitly marked as such.
    /// </summary>
    public bool IsReviewable => ScanStatus is ScanStatus.Clean or ScanStatus.SkippedDev;
}
