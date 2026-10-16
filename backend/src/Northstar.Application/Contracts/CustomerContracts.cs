namespace Northstar.Application.Contracts;

public sealed record UpdateProfileRequest(
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Occupation,
    string? AnnualIncomeBand);

public sealed record ProfileResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string? PhoneNumber,
    DateOnly? DateOfBirth,
    string? AddressLine1,
    string? AddressLine2,
    string? City,
    string? State,
    string? PostalCode,
    string? Country,
    string? Occupation,
    string? AnnualIncomeBand,
    string? NationalIdMasked,
    bool IsContactVerified,
    bool IsComplete,
    DateTimeOffset? KycVerifiedAtUtc);
