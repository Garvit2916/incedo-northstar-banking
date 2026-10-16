namespace Northstar.Application.Contracts;

public sealed record ProductResponse(
    Guid Id,
    string Code,
    string Name,
    string Summary,
    string Description,
    string Category,
    string Eligibility,
    IReadOnlyList<string> RequiredDocuments,
    string Terms,
    int MinimumAge,
    bool IsActive,
    int DisplayOrder);

public sealed record UpsertProductRequest(
    string Code,
    string Name,
    string Summary,
    string Description,
    string Category,
    string Eligibility,
    IReadOnlyList<string> RequiredDocuments,
    string Terms,
    int MinimumAge,
    bool IsActive,
    int DisplayOrder);
