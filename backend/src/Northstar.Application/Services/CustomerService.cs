using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Customer self-service profile use cases. Every operation is scoped to the
/// authenticated principal's own profile; there is deliberately no API surface
/// for reading or mutating another customer's profile.
/// </summary>
public sealed class CustomerService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IValidator<UpdateProfileRequest> _validator;

    public CustomerService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IValidator<UpdateProfileRequest> validator)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _validator = validator;
    }

    public async Task<ProfileResponse> GetMyProfileAsync(CancellationToken ct)
    {
        var profile = await RequireCurrentProfileAsync(ct);
        return profile.ToResponse(_currentUser.Email ?? string.Empty);
    }

    public async Task<ProfileResponse> UpdateMyProfileAsync(UpdateProfileRequest request, CancellationToken ct)
    {
        await _validator.ValidateAndThrowAsync(request, ct);

        var profile = await RequireCurrentProfileAsync(ct);

        profile.FirstName = request.FirstName.Trim();
        profile.LastName = request.LastName.Trim();
        profile.PhoneNumber = request.PhoneNumber?.Trim();
        profile.DateOfBirth = request.DateOfBirth;
        profile.AddressLine1 = request.AddressLine1?.Trim();
        profile.AddressLine2 = request.AddressLine2?.Trim();
        profile.City = request.City?.Trim();
        profile.State = request.State?.Trim();
        profile.PostalCode = request.PostalCode?.Trim();
        profile.Country = request.Country?.Trim();
        profile.Occupation = request.Occupation?.Trim();
        profile.AnnualIncomeBand = request.AnnualIncomeBand?.Trim();

        profile.UpdatedAtUtc = _clock.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("customer.profile.updated", "CustomerProfile", profile.Id.ToString());

        return profile.ToResponse(_currentUser.Email ?? string.Empty);
    }

    private async Task<CustomerProfile> RequireCurrentProfileAsync(CancellationToken ct)
    {
        if (_currentUser.UserId is not { } userId)
        {
            throw new ForbiddenException("Authentication is required.");
        }

        return await _db.CustomerProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct)
               ?? throw new NotFoundException("Customer profile was not found.");
    }
}
