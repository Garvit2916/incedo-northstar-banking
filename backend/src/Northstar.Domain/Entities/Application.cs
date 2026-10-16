using Northstar.Domain.Common;
using Northstar.Domain.Workflow;

namespace Northstar.Domain.Entities;

/// <summary>
/// A customer's application for a banking product. Status changes are only ever
/// performed through the guarded methods below, which consult
/// <see cref="ApplicationStateMachine"/>. Direct assignment to
/// <see cref="Status"/> outside this class is a bug.
/// </summary>
public class Application : AuditableEntity
{
    public string Reference { get; set; } = string.Empty;

    public Guid CustomerProfileId { get; set; }

    public CustomerProfile CustomerProfile { get; set; } = null!;

    public Guid ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public ApplicationStatus Status { get; private set; } = ApplicationStatus.Draft;

    /// <summary>Structured application form answers, stored as JSON.</summary>
    public string FormDataJson { get; set; } = "{}";

    public DateTimeOffset? SubmittedAtUtc { get; private set; }

    public DateTimeOffset? DecidedAtUtc { get; private set; }

    /// <summary>Reason supplied with the most recent reviewer decision.</summary>
    public string? DecisionReason { get; private set; }

    public Guid? AssignedReviewerId { get; set; }

    public ICollection<ApplicationStatusHistory> History { get; set; } = new List<ApplicationStatusHistory>();

    public ICollection<KycDocument> Documents { get; set; } = new List<KycDocument>();

    private Application()
    {
    }

    public static Application CreateDraft(
        Guid customerProfileId,
        Guid productId,
        string reference,
        string? formDataJson)
    {
        return new Application
        {
            CustomerProfileId = customerProfileId,
            ProductId = productId,
            Reference = reference,
            FormDataJson = string.IsNullOrWhiteSpace(formDataJson) ? "{}" : formDataJson,
            Status = ApplicationStatus.Draft
        };
    }

    public void Submit(Guid actingUserId, DateTimeOffset now)
    {
        Transition(ApplicationStatus.Submitted, actingUserId, null, now);
        SubmittedAtUtc = now;
    }

    public void StartReview(Guid reviewerId, DateTimeOffset now)
    {
        Transition(ApplicationStatus.UnderReview, reviewerId, null, now);
        AssignedReviewerId ??= reviewerId;
    }

    public void RequestAdditionalInformation(Guid reviewerId, string reason, DateTimeOffset now)
    {
        RequireReason(reason);
        Transition(ApplicationStatus.AdditionalInformationRequired, reviewerId, reason, now);
    }

    public void Approve(Guid reviewerId, string reason, DateTimeOffset now)
    {
        RequireReason(reason);
        Transition(ApplicationStatus.Approved, reviewerId, reason, now);
        DecidedAtUtc = now;
    }

    public void Reject(Guid reviewerId, string reason, DateTimeOffset now)
    {
        RequireReason(reason);
        Transition(ApplicationStatus.Rejected, reviewerId, reason, now);
        DecidedAtUtc = now;
    }

    public void Withdraw(Guid actingUserId, DateTimeOffset now)
    {
        Transition(ApplicationStatus.Withdrawn, actingUserId, "Withdrawn by customer.", now);
    }

    private void Transition(
        ApplicationStatus to,
        Guid actingUserId,
        string? reason,
        DateTimeOffset now)
    {
        ApplicationStateMachine.EnsureTransition(Status, to);

        var from = Status;
        Status = to;

        // Retain the most recent supplied reason so the customer-facing detail
        // response can explain a decision (or an information request) without
        // having to reconstruct it from the history trail. Transitions without a
        // reason leave the previous value intact, matching the property's intent.
        if (reason is not null)
        {
            DecisionReason = reason;
        }

        History.Add(new ApplicationStatusHistory
        {
            ApplicationId = Id,
            FromStatus = from,
            ToStatus = to,
            ChangedByUserId = actingUserId,
            Reason = reason,
            ChangedAtUtc = now
        });
    }

    private static void RequireReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A reason is required for this decision.");
        }
    }
}
