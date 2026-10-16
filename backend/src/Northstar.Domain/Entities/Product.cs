using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// A banking product offered for onboarding. Catalogue content is illustrative
/// demonstration data only and must not present invented rates or returns as
/// real financial offers.
/// </summary>
public class Product : AuditableEntity
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    /// <summary>Human-readable eligibility guidance (demonstration copy).</summary>
    public string Eligibility { get; set; } = string.Empty;

    /// <summary>Document type names required for this product, stored as JSON.</summary>
    public List<string> RequiredDocuments { get; set; } = new();

    public string Terms { get; set; } = string.Empty;

    public int MinimumAge { get; set; } = 18;

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; }

    public ICollection<Application> Applications { get; set; } = new List<Application>();
}
