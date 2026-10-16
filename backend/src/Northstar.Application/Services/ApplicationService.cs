using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain;
using Northstar.Domain.Common;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Customer onboarding workflow: draft creation, submission, tracking, and the
/// reviewer decision path. All status changes flow through the domain state
/// machine; this service is responsible for authorisation, persistence,
/// notifications, and auditing around those transitions.
/// </summary>
public sealed class ApplicationService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IReferenceGenerator _references;
    private readonly NotificationService _notifications;
    private readonly IValidator<CreateApplicationRequest> _createValidator;
    private readonly IValidator<UpdateApplicationDraftRequest> _draftValidator;
    private readonly IValidator<ReasonRequest> _reasonValidator;

    public ApplicationService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IReferenceGenerator references,
        NotificationService notifications,
        IValidator<CreateApplicationRequest> createValidator,
        IValidator<UpdateApplicationDraftRequest> draftValidator,
        IValidator<ReasonRequest> reasonValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _references = references;
        _notifications = notifications;
        _createValidator = createValidator;
        _draftValidator = draftValidator;
        _reasonValidator = reasonValidator;
    }

    // ---------------------------------------------------------------- customer

    public async Task<ApplicationDetailResponse> CreateDraftAsync(
        CreateApplicationRequest request,
        CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var profile = await RequireCurrentProfileAsync(ct);

        var product = await _db.Products
            .FirstOrDefaultAsync(p => p.Id == request.ProductId && p.IsActive, ct)
            ?? throw new NotFoundException("The requested product is not available.");

        var draft = Domain.Entities.Application.CreateDraft(
            profile.Id,
            product.Id,
            _references.NextApplicationReference(),
            request.Answers is null ? "{}" : DtoMapping.SerialiseAnswers(request.Answers));

        draft.CreatedAtUtc = _clock.UtcNow;
        draft.UpdatedAtUtc = _clock.UtcNow;

        _db.Applications.Add(draft);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync("application.created", "Application", draft.Id.ToString(), new { draft.Reference });

        return await LoadDetailAsync(draft.Id, ct);
    }

    public async Task<ApplicationDetailResponse> UpdateDraftAsync(
        Guid id,
        UpdateApplicationDraftRequest request,
        CancellationToken ct)
    {
        await _draftValidator.ValidateAndThrowAsync(request, ct);

        var application = await RequireOwnedApplicationAsync(id, ct);

        if (application.Status is not (ApplicationStatus.Draft or ApplicationStatus.AdditionalInformationRequired))
        {
            throw new ConflictException("This application can no longer be edited.");
        }

        application.FormDataJson = DtoMapping.SerialiseAnswers(request.Answers);
        application.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("application.draft.updated", "Application", application.Id.ToString());

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<ApplicationDetailResponse> SubmitAsync(Guid id, CancellationToken ct)
    {
        var application = await RequireOwnedApplicationAsync(id, ct, includeProduct: true);
        var profile = await RequireCurrentProfileAsync(ct);

        if (!profile.IsComplete)
        {
            throw new ConflictException(
                "Complete your profile before submitting an application.");
        }

        var answers = DtoMapping.ParseAnswers(application.FormDataJson);
        if (answers.Count == 0)
        {
            throw new ConflictException("Complete the application form before submitting.");
        }

        EnsureEligibility(profile, application.Product);

        var now = _clock.UtcNow;
        await using var tx = await _db.BeginTransactionAsync(ct);

        application.Submit(_currentUser.UserId!.Value, now);
        application.UpdatedAtUtc = now;

        _notifications.EnqueueNotification(
            profile.UserId,
            application.Id,
            NotificationType.ApplicationSubmitted,
            "Application submitted",
            $"Your application {application.Reference} has been submitted and will be reviewed.",
            $"application:{application.Id}:submitted:{application.History.Count}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.RecordAsync("application.submitted", "Application", application.Id.ToString(), new { application.Reference });

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<ApplicationDetailResponse> WithdrawAsync(Guid id, CancellationToken ct)
    {
        var application = await RequireOwnedApplicationAsync(id, ct);
        var now = _clock.UtcNow;

        application.Withdraw(_currentUser.UserId!.Value, now);
        application.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("application.withdrawn", "Application", application.Id.ToString());

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<PagedResult<ApplicationSummaryResponse>> ListMineAsync(PageQuery page, CancellationToken ct)
    {
        var userId = RequireUserId();

        var query = _db.Applications
            .AsNoTracking()
            .Include(a => a.Product)
            .Where(a => a.CustomerProfile!.UserId == userId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize)
            .ToListAsync(ct);

        return new PagedResult<ApplicationSummaryResponse>(
            items.Select(a => a.ToSummary()).ToList(),
            page.NormalisedPage,
            page.NormalisedPageSize,
            total);
    }

    public async Task<ApplicationDetailResponse> GetMineAsync(Guid id, CancellationToken ct)
    {
        var application = await RequireOwnedApplicationAsync(id, ct);
        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<IReadOnlyList<ApplicationStatusHistoryResponse>> GetMineHistoryAsync(
        Guid id,
        CancellationToken ct)
    {
        var application = await RequireOwnedApplicationAsync(id, ct);
        return application.History
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => h.ToResponse())
            .ToList();
    }

    // ---------------------------------------------------------------- reviewer

    public async Task<PagedResult<ApplicationSummaryResponse>> ListReviewQueueAsync(
        ApplicationStatus? status,
        string? search,
        PageQuery page,
        CancellationToken ct)
    {
        var query = _db.Applications
            .AsNoTracking()
            .Include(a => a.Product)
            .AsQueryable();

        if (status is not null)
        {
            query = query.Where(a => a.Status == status);
        }
        else
        {
            query = query.Where(a => a.Status == ApplicationStatus.Submitted
                                     || a.Status == ApplicationStatus.UnderReview
                                     || a.Status == ApplicationStatus.AdditionalInformationRequired);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(a => a.Reference.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(a => a.SubmittedAtUtc)
            .ThenBy(a => a.CreatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize)
            .ToListAsync(ct);

        return new PagedResult<ApplicationSummaryResponse>(
            items.Select(a => a.ToSummary()).ToList(),
            page.NormalisedPage,
            page.NormalisedPageSize,
            total);
    }

    public async Task<ApplicationDetailResponse> GetForReviewAsync(Guid id, CancellationToken ct) =>
        await LoadDetailAsync(id, ct);

    public async Task<ApplicationDetailResponse> StartReviewAsync(Guid id, CancellationToken ct)
    {
        var application = await RequireReviewableApplicationAsync(id, ct);
        var now = _clock.UtcNow;

        application.StartReview(RequireUserId(), now);
        application.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("application.review.started", "Application", application.Id.ToString());

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<ApplicationDetailResponse> RequestAdditionalInformationAsync(
        Guid id,
        ReasonRequest request,
        CancellationToken ct)
    {
        await _reasonValidator.ValidateAndThrowAsync(request, ct);

        var application = await RequireReviewableApplicationAsync(id, ct);
        var customerUserId = await ResolveCustomerUserIdAsync(application.CustomerProfileId, ct);
        var now = _clock.UtcNow;

        await using var tx = await _db.BeginTransactionAsync(ct);

        application.RequestAdditionalInformation(RequireUserId(), request.Reason.Trim(), now);
        application.UpdatedAtUtc = now;

        _notifications.EnqueueNotification(
            customerUserId,
            application.Id,
            NotificationType.AdditionalInformationRequired,
            "More information needed",
            $"We need additional information to continue reviewing application {application.Reference}. " +
            "Please sign in to see the details.",
            $"application:{application.Id}:addinfo:{application.History.Count}");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.RecordAsync("application.information.requested", "Application", application.Id.ToString());

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<ApplicationDetailResponse> ApproveAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await _reasonValidator.ValidateAndThrowAsync(request, ct);

        var application = await RequireReviewableApplicationAsync(id, ct);
        var customerUserId = await ResolveCustomerUserIdAsync(application.CustomerProfileId, ct);
        var now = _clock.UtcNow;

        await using var tx = await _db.BeginTransactionAsync(ct);

        application.Approve(RequireUserId(), request.Reason.Trim(), now);
        application.UpdatedAtUtc = now;

        _notifications.EnqueueNotification(
            customerUserId,
            application.Id,
            NotificationType.ApplicationApproved,
            "Application approved",
            $"Your application {application.Reference} has been approved.",
            $"application:{application.Id}:approved");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.RecordAsync("application.approved", "Application", application.Id.ToString(), new { application.Reference });

        return await LoadDetailAsync(application.Id, ct);
    }

    public async Task<ApplicationDetailResponse> RejectAsync(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await _reasonValidator.ValidateAndThrowAsync(request, ct);

        var application = await RequireReviewableApplicationAsync(id, ct);
        var customerUserId = await ResolveCustomerUserIdAsync(application.CustomerProfileId, ct);
        var now = _clock.UtcNow;

        await using var tx = await _db.BeginTransactionAsync(ct);

        application.Reject(RequireUserId(), request.Reason.Trim(), now);
        application.UpdatedAtUtc = now;

        _notifications.EnqueueNotification(
            customerUserId,
            application.Id,
            NotificationType.ApplicationRejected,
            "Application update",
            $"We were unable to approve application {application.Reference}. " +
            "Please sign in to see the reason and your options.",
            $"application:{application.Id}:rejected");

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await _audit.RecordAsync("application.rejected", "Application", application.Id.ToString(), new { application.Reference });

        return await LoadDetailAsync(application.Id, ct);
    }

    // ----------------------------------------------------------------- helpers

    private void EnsureEligibility(CustomerProfile profile, Product product)
    {
        if (profile.DateOfBirth is { } dob)
        {
            var today = DateOnly.FromDateTime(_clock.UtcNow.UtcDateTime);
            var age = today.Year - dob.Year;
            if (dob > today.AddYears(-age))
            {
                age--;
            }

            if (age < product.MinimumAge)
            {
                throw new ConflictException(
                    $"This product requires applicants to be at least {product.MinimumAge} years old.");
            }
        }
    }

    private async Task<ApplicationDetailResponse> LoadDetailAsync(Guid id, CancellationToken ct)
    {
        var application = await _db.Applications
            .AsNoTracking()
            .Include(a => a.Product)
            .Include(a => a.History)
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Application was not found.");

        return application.ToDetail();
    }

    private async Task<Domain.Entities.Application> RequireOwnedApplicationAsync(
        Guid id,
        CancellationToken ct,
        bool includeProduct = false)
    {
        var userId = RequireUserId();

        var query = _db.Applications
            .Include(a => a.CustomerProfile)
            .Include(a => a.History)
            .Include(a => a.Documents)
            .AsQueryable();

        if (includeProduct)
        {
            query = query.Include(a => a.Product);
        }

        var application = await query.FirstOrDefaultAsync(a => a.Id == id, ct);

        if (application is null || application.CustomerProfile is null ||
            application.CustomerProfile.UserId != userId)
        {
            // Return 404 rather than 403 so a customer cannot enumerate other
            // customers' application identifiers.
            throw new NotFoundException("Application was not found.");
        }

        return application;
    }

    private async Task<Domain.Entities.Application> RequireReviewableApplicationAsync(Guid id, CancellationToken ct)
    {
        var application = await _db.Applications
            .Include(a => a.History)
            .Include(a => a.Documents)
            .FirstOrDefaultAsync(a => a.Id == id, ct)
            ?? throw new NotFoundException("Application was not found.");

        return application;
    }

    private async Task<Guid> ResolveCustomerUserIdAsync(Guid customerProfileId, CancellationToken ct) =>
        await _db.CustomerProfiles
            .Where(p => p.Id == customerProfileId)
            .Select(p => p.UserId)
            .FirstAsync(ct);

    private async Task<CustomerProfile> RequireCurrentProfileAsync(CancellationToken ct)
    {
        var userId = RequireUserId();
        return await _db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
               ?? throw new NotFoundException("Customer profile was not found.");
    }

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
