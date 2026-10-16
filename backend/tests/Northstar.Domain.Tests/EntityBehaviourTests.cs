using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Domain.Tests;

/// <summary>
/// Small value rules that are easy to regress: notification read bookkeeping,
/// refresh-token validity, document reviewability, and profile completeness.
/// </summary>
public sealed class EntityBehaviourTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    // ------------------------------------------------------------- Notification

    [Fact]
    public void Marking_a_notification_read_stamps_the_time()
    {
        var notification = new Notification { Title = "Approved", Body = "Your application was approved." };

        notification.MarkRead(Now);

        Assert.True(notification.IsRead);
        Assert.Equal(Now, notification.ReadAtUtc);
    }

    [Fact]
    public void Marking_an_already_read_notification_changes_nothing()
    {
        var notification = new Notification();
        notification.MarkRead(Now);

        notification.MarkRead(Now.AddHours(1));

        // The first read timestamp must be preserved rather than overwritten.
        Assert.Equal<DateTimeOffset?>(Now, notification.ReadAtUtc);
    }

    // ------------------------------------------------------------- RefreshToken

    [Fact]
    public void Refresh_token_is_active_before_expiry()
    {
        var token = new RefreshToken { ExpiresAtUtc = Now.AddMinutes(30) };

        Assert.True(token.IsActive(Now));
    }

    [Fact]
    public void Refresh_token_is_inactive_once_expired()
    {
        var token = new RefreshToken { ExpiresAtUtc = Now.AddSeconds(-1) };

        Assert.False(token.IsActive(Now));
    }

    [Fact]
    public void Refresh_token_is_inactive_once_revoked()
    {
        var token = new RefreshToken
        {
            ExpiresAtUtc = Now.AddHours(1),
            RevokedAtUtc = Now
        };

        Assert.False(token.IsActive(Now));
    }

    // ------------------------------------------------------------ KycDocument

    [Theory]
    [InlineData(ScanStatus.Clean, true)]
    [InlineData(ScanStatus.SkippedDev, true)]
    [InlineData(ScanStatus.NotScanned, false)]
    [InlineData(ScanStatus.Infected, false)]
    [InlineData(ScanStatus.ScanFailed, false)]
    public void Only_scanned_documents_are_reviewable(ScanStatus scanStatus, bool reviewable)
    {
        var document = new KycDocument { ScanStatus = scanStatus };

        Assert.Equal(reviewable, document.IsReviewable);
    }

    // --------------------------------------------------------- CustomerProfile

    private static CustomerProfile CompleteProfile() => new()
    {
        FirstName = "Ada",
        LastName = "Lovelace",
        PhoneNumber = "+1-555-0100",
        DateOfBirth = new DateOnly(1990, 5, 4),
        AddressLine1 = "1 Computing Lane",
        City = "London",
        PostalCode = "EC1A 1BB",
        Country = "United Kingdom"
    };

    [Fact]
    public void A_fully_populated_profile_is_complete()
    {
        Assert.True(CompleteProfile().IsComplete);
    }

    [Theory]
    [InlineData(nameof(CustomerProfile.FirstName))]
    [InlineData(nameof(CustomerProfile.LastName))]
    [InlineData(nameof(CustomerProfile.PhoneNumber))]
    [InlineData(nameof(CustomerProfile.AddressLine1))]
    [InlineData(nameof(CustomerProfile.City))]
    [InlineData(nameof(CustomerProfile.PostalCode))]
    [InlineData(nameof(CustomerProfile.Country))]
    public void A_profile_missing_a_required_field_is_incomplete(string fieldToClear)
    {
        var profile = CompleteProfile();
        typeof(CustomerProfile).GetProperty(fieldToClear)!.SetValue(profile, null);

        Assert.False(profile.IsComplete);
    }

    [Fact]
    public void A_profile_without_a_date_of_birth_is_incomplete()
    {
        var profile = CompleteProfile();
        profile.DateOfBirth = null;

        Assert.False(profile.IsComplete);
    }

    [Fact]
    public void Full_name_is_trimmed()
    {
        var profile = new CustomerProfile { FirstName = "Ada", LastName = "Lovelace" };

        Assert.Equal("Ada Lovelace", profile.FullName);
    }

    // -------------------------------------------------------- ApplicationStatus

    [Fact]
    public void Application_status_values_are_stable()
    {
        // Guard against an enum reorder silently changing the stored migration data.
        Assert.Equal(0, (int)ApplicationStatus.Draft);
        Assert.Equal(1, (int)ApplicationStatus.Submitted);
        Assert.Equal(2, (int)ApplicationStatus.UnderReview);
        Assert.Equal(3, (int)ApplicationStatus.AdditionalInformationRequired);
        Assert.Equal(4, (int)ApplicationStatus.Approved);
        Assert.Equal(5, (int)ApplicationStatus.Rejected);
        Assert.Equal(6, (int)ApplicationStatus.Withdrawn);
    }
}
