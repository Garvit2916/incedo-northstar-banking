using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// Personal and contact details a customer may maintain for themselves. Only
/// fields required for the assessment workflows are stored. Government
/// identifiers are never persisted in full - only a masked reference.
/// </summary>
public class CustomerProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    public string? AddressLine1 { get; set; }

    public string? AddressLine2 { get; set; }

    public string? City { get; set; }

    public string? State { get; set; }

    public string? PostalCode { get; set; }

    public string? Country { get; set; }

    public string? Occupation { get; set; }

    /// <summary>Illustrative income band, not a verified income figure.</summary>
    public string? AnnualIncomeBand { get; set; }

    /// <summary>Masked identifier such as "****1234"; never the full value.</summary>
    public string? NationalIdMasked { get; set; }

    public bool IsContactVerified { get; set; }

    public DateTimeOffset? KycVerifiedAtUtc { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();

    public ICollection<KycDocument> Documents { get; set; } = new List<KycDocument>();

    public string FullName => $"{FirstName} {LastName}".Trim();

    /// <summary>
    /// A profile is considered complete when the minimum set of fields required
    /// to submit an application is present. Used to surface the incomplete state
    /// in the UI without blocking browsing.
    /// </summary>
    public bool IsComplete =>
        !string.IsNullOrWhiteSpace(FirstName) &&
        !string.IsNullOrWhiteSpace(LastName) &&
        !string.IsNullOrWhiteSpace(PhoneNumber) &&
        DateOfBirth is not null &&
        !string.IsNullOrWhiteSpace(AddressLine1) &&
        !string.IsNullOrWhiteSpace(City) &&
        !string.IsNullOrWhiteSpace(PostalCode) &&
        !string.IsNullOrWhiteSpace(Country);
}
