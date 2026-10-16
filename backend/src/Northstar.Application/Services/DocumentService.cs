using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Configuration;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Secure KYC document handling. Documents are validated server-side, stored
/// under server-generated keys in private storage, and only ever exposed to their
/// owner or an authorised reviewer.
/// </summary>
public sealed class DocumentService
{
    private static readonly TimeSpan LinkLifetime = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IBlobStorage _storage;
    private readonly IFileScanner _scanner;
    private readonly NotificationService _notifications;
    private readonly FileUploadOptions _options;
    private readonly IValidator<DocumentReviewRequest> _reviewValidator;

    public DocumentService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IBlobStorage storage,
        IFileScanner scanner,
        NotificationService notifications,
        IOptions<FileUploadOptions> options,
        IValidator<DocumentReviewRequest> reviewValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _storage = storage;
        _scanner = scanner;
        _notifications = notifications;
        _options = options.Value;
        _reviewValidator = reviewValidator;
    }

    public async Task<DocumentResponse> UploadAsync(Guid applicationId, DocumentUpload upload, CancellationToken ct)
    {
        var application = await RequireOwnedApplicationAsync(applicationId, ct);

        if (application.Status is ApplicationStatus.Approved or ApplicationStatus.Rejected or ApplicationStatus.Withdrawn)
        {
            throw new ConflictException("Documents cannot be added to a closed application.");
        }

        var content = await ReadAndValidateAsync(upload, ct);

        var scanStatus = await _scanner.ScanAsync(new MemoryStream(content.Bytes), upload.ContentType, ct);

        var extension = Path.GetExtension(upload.OriginalFileName).ToLowerInvariant();
        var storageKey = $"kyc/{application.CustomerProfileId}/{application.Id}/{Guid.NewGuid():N}{extension}";

        await using (var saveStream = new MemoryStream(content.Bytes))
        {
            await _storage.SaveAsync(saveStream, storageKey, upload.ContentType, ct);
        }

        var now = _clock.UtcNow;
        var document = new KycDocument
        {
            ApplicationId = application.Id,
            CustomerProfileId = application.CustomerProfileId,
            DocumentType = upload.DocumentType,
            OriginalFileName = SanitiseFileName(upload.OriginalFileName),
            StorageKey = storageKey,
            ContentType = upload.ContentType,
            SizeBytes = content.Bytes.Length,
            Sha256 = content.Sha256,
            ScanStatus = scanStatus,
            ReviewStatus = DocumentReviewStatus.Pending,
            UploadedAtUtc = now,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        _db.KycDocuments.Add(document);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync("document.uploaded", "KycDocument", document.Id.ToString(), new
        {
            document.DocumentType,
            document.SizeBytes,
            document.ScanStatus
        });

        return document.ToResponse();
    }

    public async Task<IReadOnlyList<DocumentResponse>> ListForApplicationAsync(Guid applicationId, CancellationToken ct)
    {
        await RequireVisibleApplicationAsync(applicationId, ct);

        var documents = await _db.KycDocuments
            .AsNoTracking()
            .Where(d => d.ApplicationId == applicationId)
            .OrderByDescending(d => d.UploadedAtUtc)
            .ToListAsync(ct);

        return documents.Select(d => d.ToResponse()).ToList();
    }

    public async Task<DocumentResponse> ReviewAsync(Guid documentId, DocumentReviewRequest request, CancellationToken ct)
    {
        await _reviewValidator.ValidateAndThrowAsync(request, ct);

        var document = await _db.KycDocuments.FirstOrDefaultAsync(d => d.Id == documentId, ct)
                       ?? throw new NotFoundException("Document was not found.");

        if (!document.IsReviewable)
        {
            throw new ConflictException("This document is not available for review until scanning completes.");
        }

        var now = _clock.UtcNow;
        document.ReviewStatus = request.Decision;
        document.ReviewNotes = request.Notes?.Trim();
        document.ReviewedByUserId = RequireUserId();
        document.ReviewedAtUtc = now;
        document.UpdatedAtUtc = now;

        var customerUserId = await ResolveCustomerUserIdAsync(document.CustomerProfileId, ct);
        _notifications.EnqueueNotification(
            customerUserId,
            document.ApplicationId,
            NotificationType.DocumentReviewed,
            "Document reviewed",
            $"A document you submitted was reviewed ({request.Decision}).",
            $"document:{document.Id}:reviewed:{request.Decision}");

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("document.reviewed", "KycDocument", document.Id.ToString(), new { request.Decision });

        return document.ToResponse();
    }

    public async Task<DownloadLinkResponse> GetDownloadLinkAsync(Guid documentId, CancellationToken ct)
    {
        var document = await RequireAccessibleDocumentAsync(documentId, ct);

        var presigned = await _storage.GetPresignedUrlAsync(document.StorageKey, LinkLifetime, ct);
        var url = presigned?.ToString() ?? $"/api/documents/{document.Id}/content";

        return new DownloadLinkResponse(url, document.OriginalFileName, _clock.UtcNow.Add(LinkLifetime));
    }

    public async Task<DocumentContent> OpenContentAsync(Guid documentId, CancellationToken ct)
    {
        var document = await RequireAccessibleDocumentAsync(documentId, ct);
        var stream = await _storage.OpenReadAsync(document.StorageKey, ct);

        await _audit.RecordAsync("document.downloaded", "KycDocument", document.Id.ToString());

        return new DocumentContent(stream, document.OriginalFileName, document.ContentType);
    }

    // ------------------------------------------------------------------ helpers

    private async Task<ValidatedUpload> ReadAndValidateAsync(DocumentUpload upload, CancellationToken ct)
    {
        var extension = Path.GetExtension(upload.OriginalFileName).ToLowerInvariant();

        if (!_options.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("This file type is not accepted. Allowed: " +
                                                 string.Join(", ", _options.AllowedExtensions));
        }

        if (!_options.AllowedContentTypes.Contains(upload.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new RequestValidationException("The declared content type is not accepted.");
        }

        if (upload.Length <= 0 || upload.Length > _options.MaxFileSizeBytes)
        {
            throw new RequestValidationException($"The file must be larger than 0 and no more than {_options.MaxFileSizeBytes / (1024 * 1024)} MB.");
        }

        var bytes = new byte[upload.Length];
        var read = 0;
        while (read < bytes.Length)
        {
            var chunk = await upload.Content.ReadAsync(bytes.AsMemory(read, bytes.Length - read), ct);
            if (chunk == 0)
            {
                break;
            }

            read += chunk;
        }

        if (read != bytes.Length)
        {
            throw new RequestValidationException("The uploaded file was incomplete.");
        }

        if (!FileSignatureValidator.Matches(upload.ContentType, bytes.AsSpan(0, Math.Min(8, bytes.Length))))
        {
            throw new RequestValidationException("The file content does not match its declared type. The upload was rejected.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        return new ValidatedUpload(bytes, hash);
    }

    private static string SanitiseFileName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        return string.IsNullOrWhiteSpace(name) ? "document" : name[..Math.Min(name.Length, 200)];
    }

    private async Task<Domain.Entities.Application> RequireOwnedApplicationAsync(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var application = await _db.Applications
            .Include(a => a.CustomerProfile)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

        if (application?.CustomerProfile is null || application.CustomerProfile.UserId != userId)
        {
            throw new NotFoundException("Application was not found.");
        }

        return application;
    }

    /// <summary>A customer may see only their own application's documents; reviewers see all.</summary>
    private async Task RequireVisibleApplicationAsync(Guid id, CancellationToken ct)
    {
        if (_currentUser.IsInRole(Roles.Reviewer) || _currentUser.IsInRole(Roles.Administrator))
        {
            if (!await _db.Applications.AnyAsync(a => a.Id == id, ct))
            {
                throw new NotFoundException("Application was not found.");
            }

            return;
        }

        await RequireOwnedApplicationAsync(id, ct);
    }

    private async Task<KycDocument> RequireAccessibleDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var document = await _db.KycDocuments
            .Include(d => d.CustomerProfile)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct)
            ?? throw new NotFoundException("Document was not found.");

        var isPrivileged = _currentUser.IsInRole(Roles.Reviewer) || _currentUser.IsInRole(Roles.Administrator);
        if (!isPrivileged && document.CustomerProfile?.UserId != RequireUserId())
        {
            throw new NotFoundException("Document was not found.");
        }

        return document;
    }

    private async Task<Guid> ResolveCustomerUserIdAsync(Guid customerProfileId, CancellationToken ct) =>
        await _db.CustomerProfiles
            .Where(p => p.Id == customerProfileId)
            .Select(p => p.UserId)
            .FirstAsync(ct);

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");

    private sealed record ValidatedUpload(byte[] Bytes, string Sha256);
}
