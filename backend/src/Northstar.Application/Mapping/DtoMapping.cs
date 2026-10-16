using System.Text.Json;
using Northstar.Application.Contracts;
using Northstar.Domain.Entities;

namespace Northstar.Application.Mapping;

/// <summary>
/// Central mapping between domain entities and API contracts. Keeping this in one
/// place prevents accidental exposure of internal entity fields (over-posting and
/// information-leak risks) from being introduced ad hoc in controllers.
/// </summary>
public static class DtoMapping
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static ProfileResponse ToResponse(this CustomerProfile p, string email) => new(
        p.UserId,
        email,
        p.FirstName,
        p.LastName,
        p.PhoneNumber,
        p.DateOfBirth,
        p.AddressLine1,
        p.AddressLine2,
        p.City,
        p.State,
        p.PostalCode,
        p.Country,
        p.Occupation,
        p.AnnualIncomeBand,
        p.NationalIdMasked,
        p.IsContactVerified,
        p.IsComplete,
        p.KycVerifiedAtUtc);

    public static ProductResponse ToResponse(this Product p) => new(
        p.Id,
        p.Code,
        p.Name,
        p.Summary,
        p.Description,
        p.Category,
        p.Eligibility,
        p.RequiredDocuments,
        p.Terms,
        p.MinimumAge,
        p.IsActive,
        p.DisplayOrder);

    public static ApplicationSummaryResponse ToSummary(this Domain.Entities.Application a) => new(
        a.Id,
        a.Reference,
        a.ProductId,
        a.Product?.Name ?? string.Empty,
        a.Status,
        a.CreatedAtUtc,
        a.SubmittedAtUtc,
        a.DecidedAtUtc);

    public static ApplicationStatusHistoryResponse ToResponse(this ApplicationStatusHistory h) => new(
        h.FromStatus,
        h.ToStatus,
        h.ChangedByUserId,
        h.Reason,
        h.ChangedAtUtc);

    public static DocumentResponse ToResponse(this KycDocument d) => new(
        d.Id,
        d.ApplicationId,
        d.DocumentType,
        d.OriginalFileName,
        d.ContentType,
        d.SizeBytes,
        d.ScanStatus,
        d.ReviewStatus,
        d.ReviewNotes,
        d.UploadedAtUtc,
        d.ReviewedAtUtc,
        d.IsReviewable);

    public static ApplicationDetailResponse ToDetail(this Domain.Entities.Application a) => new(
        a.Id,
        a.Reference,
        a.ProductId,
        a.Product?.Name ?? string.Empty,
        a.Status,
        ParseAnswers(a.FormDataJson),
        a.DecisionReason,
        a.AssignedReviewerId,
        a.CreatedAtUtc,
        a.SubmittedAtUtc,
        a.DecidedAtUtc,
        a.History
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => h.ToResponse())
            .ToList(),
        a.Documents
            .OrderByDescending(d => d.UploadedAtUtc)
            .Select(d => d.ToResponse())
            .ToList());

    public static NotificationResponse ToResponse(this Notification n) => new(
        n.Id,
        n.Type,
        n.Title,
        n.Body,
        n.IsRead,
        n.ApplicationId,
        n.CreatedAtUtc,
        n.ReadAtUtc);

    public static TicketMessageResponse ToResponse(this TicketMessage m) => new(
        m.Id,
        m.AuthorUserId,
        m.AuthorType,
        m.Body,
        m.IsInternalNote,
        m.CreatedAtUtc);

    public static TicketSummaryResponse ToSummary(this Ticket t, int messageCount) => new(
        t.Id,
        t.Reference,
        t.Subject,
        t.Category,
        t.Priority,
        t.Status,
        t.CustomerUserId,
        t.AssignedAgentId,
        t.RelatedApplicationId,
        t.SlaDueAtUtc,
        t.ResolvedAtUtc,
        t.CreatedAtUtc,
        t.UpdatedAtUtc,
        messageCount);

    public static TicketDetailResponse ToDetail(this Ticket t) => new(
        t.Id,
        t.Reference,
        t.Subject,
        t.Description,
        t.Category,
        t.Priority,
        t.Status,
        t.CustomerUserId,
        t.AssignedAgentId,
        t.RelatedApplicationId,
        t.SlaDueAtUtc,
        t.ResolvedAtUtc,
        t.CreatedAtUtc,
        t.UpdatedAtUtc,
        t.Messages
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => m.ToResponse())
            .ToList());

    public static IReadOnlyDictionary<string, string> ParseAnswers(string? formDataJson)
    {
        if (string.IsNullOrWhiteSpace(formDataJson))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(formDataJson, JsonOptions)
                   ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }

    public static string SerialiseAnswers(IReadOnlyDictionary<string, string> answers) =>
        JsonSerializer.Serialize(answers, JsonOptions);
}
