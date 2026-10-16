using Microsoft.EntityFrameworkCore;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers the customer and reviewer application use cases end to end through the
/// real EF Core stack: draft creation, submission guards, the reviewer decision
/// path, cross-customer isolation, and notification side effects.
/// </summary>
public sealed class ApplicationServiceTests : IDisposable
{
    private static readonly Dictionary<string, string> Answers = new()
    {
        ["employmentStatus"] = "Employed",
        ["monthlyIncomeBand"] = "5000-10000"
    };

    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    // -------------------------------------------------------------- scenarios

    [Fact]
    public async Task Creating_a_draft_stores_answers_and_the_generated_reference()
    {
        var profile = _fx.SeedProfile();
        _fx.Caller.UserId = profile.UserId;
        var product = _fx.SeedProduct();

        var draft = await _fx.Applications.CreateDraftAsync(
            new CreateApplicationRequest(product.Id, Answers), default);

        Assert.Equal(ApplicationStatus.Draft, draft.Status);
        Assert.Equal("NS-APP-T000001", draft.Reference);
        Assert.Equal("Employed", draft.Answers["employmentStatus"]);
        Assert.Equal(product.Id, draft.ProductId);
        Assert.Contains("application.created", _fx.Audit.Actions);

        var stored = await _fx.Db.Applications.AsNoTracking().SingleAsync();
        Assert.Equal(profile.Id, stored.CustomerProfileId);
    }

    [Fact]
    public async Task Creating_a_draft_requires_a_customer_profile()
    {
        _fx.Caller.UserId = Guid.NewGuid();
        var product = _fx.SeedProduct();

        await Assert.ThrowsAsync<NotFoundException>(() => _fx.Applications.CreateDraftAsync(
            new CreateApplicationRequest(product.Id, Answers), default));
    }

    [Fact]
    public async Task Creating_a_draft_requires_an_active_product()
    {
        var profile = _fx.SeedProfile();
        _fx.Caller.UserId = profile.UserId;
        var product = _fx.SeedProduct(isActive: false);

        await Assert.ThrowsAsync<NotFoundException>(() => _fx.Applications.CreateDraftAsync(
            new CreateApplicationRequest(product.Id, Answers), default));
    }

    [Fact]
    public async Task Updating_a_draft_after_submission_is_rejected()
    {
        var scenario = await CreateDraftAsync();
        await _fx.Applications.SubmitAsync(scenario.DraftId, default);

        await Assert.ThrowsAsync<ConflictException>(() => _fx.Applications.UpdateDraftAsync(
            scenario.DraftId, new UpdateApplicationDraftRequest(Answers), default));
    }

    [Fact]
    public async Task Submitting_with_an_incomplete_profile_conflicts()
    {
        var scenario = await CreateDraftAsync(completeProfile: false);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => _fx.Applications.SubmitAsync(scenario.DraftId, default));

        Assert.Contains("profile", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("application.submitted", _fx.Audit.Actions);
    }

    [Fact]
    public async Task Submitting_without_any_answers_conflicts()
    {
        var scenario = await CreateDraftAsync(blankAnswers: true);

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => _fx.Applications.SubmitAsync(scenario.DraftId, default));

        Assert.Contains("form", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Submitting_records_history_notification_and_audit()
    {
        var scenario = await CreateDraftAsync();

        var submitted = await _fx.Applications.SubmitAsync(scenario.DraftId, default);

        Assert.Equal(ApplicationStatus.Submitted, submitted.Status);
        Assert.NotNull(submitted.SubmittedAtUtc);

        var history = Assert.Single(submitted.History);
        Assert.Equal(ApplicationStatus.Draft, history.FromStatus);
        Assert.Equal(ApplicationStatus.Submitted, history.ToStatus);
        Assert.Equal(scenario.Profile.UserId, history.ChangedByUserId);

        Assert.Contains("application.submitted", _fx.Audit.Actions);

        var notification = Assert.Single(await _fx.Db.Notifications.AsNoTracking().ToListAsync());
        Assert.Equal(scenario.Profile.UserId, notification.UserId);
        Assert.Equal(NotificationType.ApplicationSubmitted, notification.Type);

        var outbox = Assert.Single(await _fx.Db.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Equal("email.notification", outbox.Type);
        Assert.Equal(OutboxStatus.Pending, outbox.Status);
    }

    [Fact]
    public async Task Submitting_twice_is_rejected_by_the_state_machine()
    {
        var scenario = await CreateDraftAsync();
        await _fx.Applications.SubmitAsync(scenario.DraftId, default);

        await Assert.ThrowsAsync<DomainException>(
            () => _fx.Applications.SubmitAsync(scenario.DraftId, default));
    }

    [Fact]
    public async Task Another_customers_application_is_reported_as_missing()
    {
        var scenario = await CreateDraftAsync();

        // A different, authenticated customer must see 404 rather than any detail.
        _fx.Caller.UserId = Guid.NewGuid();

        await Assert.ThrowsAsync<NotFoundException>(
            () => _fx.Applications.GetMineAsync(scenario.DraftId, default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _fx.Applications.WithdrawAsync(scenario.DraftId, default));
    }

    [Fact]
    public async Task Listing_mine_only_returns_the_callers_applications()
    {
        await CreateDraftAsync();

        var otherProfile = _fx.SeedProfile();
        _fx.Caller.UserId = otherProfile.UserId;
        var otherProduct = _fx.SeedProduct();
        var otherDraft = await _fx.Applications.CreateDraftAsync(
            new CreateApplicationRequest(otherProduct.Id, Answers), default);

        var page = await _fx.Applications.ListMineAsync(new PageQuery(1, 50), default);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal(otherDraft.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task Product_minimum_age_is_enforced_before_submission()
    {
        var scenario = await CreateDraftAsync(
            minimumAge: 30, dateOfBirth: new DateOnly(2010, 1, 1));

        var error = await Assert.ThrowsAsync<ConflictException>(
            () => _fx.Applications.SubmitAsync(scenario.DraftId, default));

        Assert.Contains("30", error.Message);
    }

    [Fact]
    public async Task Approving_before_the_review_starts_is_rejected_by_the_state_machine()
    {
        await CreateSubmittedDraftAsync();
        UseReviewer();

        await Assert.ThrowsAsync<DomainException>(() => _fx.Applications.ApproveAsync(
            _draftId, new ReasonRequest("All identity checks passed."), default));
    }

    [Fact]
    public async Task Approving_records_the_decision_reason_and_notifies_the_customer()
    {
        var scenario = await CreateSubmittedDraftAsync();
        var reviewerId = UseReviewer();

        await _fx.Applications.StartReviewAsync(_draftId, default);
        var approved = await _fx.Applications.ApproveAsync(
            _draftId, new ReasonRequest("All identity checks passed."), default);

        Assert.Equal(ApplicationStatus.Approved, approved.Status);
        Assert.Equal("All identity checks passed.", approved.DecisionReason);
        Assert.NotNull(approved.DecidedAtUtc);

        var decision = Assert.Single(approved.History, h => h.ToStatus == ApplicationStatus.Approved);
        Assert.Equal("All identity checks passed.", decision.Reason);
        Assert.Equal(reviewerId, decision.ChangedByUserId);

        Assert.Contains("application.review.started", _fx.Audit.Actions);
        Assert.Contains("application.approved", _fx.Audit.Actions);

        var notifications = await _fx.Db.Notifications.AsNoTracking().ToListAsync();
        Assert.Equal(2, notifications.Count);
        Assert.Single(notifications, n => n.Type == NotificationType.ApplicationApproved);
        Assert.All(notifications, n => Assert.Equal(scenario.Profile.UserId, n.UserId));
        Assert.Equal(2, await _fx.Db.OutboxMessages.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task Rejecting_records_the_decision_reason()
    {
        await CreateSubmittedDraftAsync();
        UseReviewer();

        await _fx.Applications.StartReviewAsync(_draftId, default);
        var rejected = await _fx.Applications.RejectAsync(
            _draftId, new ReasonRequest("Income evidence was inconclusive."), default);

        Assert.Equal(ApplicationStatus.Rejected, rejected.Status);
        Assert.Equal("Income evidence was inconclusive.", rejected.DecisionReason);
        Assert.Contains("application.rejected", _fx.Audit.Actions);
        Assert.Single(await _fx.Db.Notifications.AsNoTracking()
            .Where(n => n.Type == NotificationType.ApplicationRejected).ToListAsync());
    }

    [Fact]
    public async Task A_reason_shorter_than_five_characters_fails_validation()
    {
        await CreateSubmittedDraftAsync();
        UseReviewer();

        await Assert.ThrowsAsync<FluentValidation.ValidationException>(() =>
            _fx.Applications.ApproveAsync(_draftId, new ReasonRequest("ok"), default));
    }

    [Fact]
    public async Task Withdrawing_a_submitted_application_records_the_default_reason()
    {
        var scenario = await CreateDraftAsync();
        await _fx.Applications.SubmitAsync(scenario.DraftId, default);

        var withdrawn = await _fx.Applications.WithdrawAsync(scenario.DraftId, default);

        Assert.Equal(ApplicationStatus.Withdrawn, withdrawn.Status);
        Assert.False(string.IsNullOrWhiteSpace(withdrawn.DecisionReason));
        Assert.Contains("application.withdrawn", _fx.Audit.Actions);
    }

    // ---------------------------------------------------------------- helpers

    private sealed record Scenario(CustomerProfile Profile, Product Product, Guid DraftId);

    /// <summary>Draft id of the most recently created scenario in this test.</summary>
    private Guid _draftId;

    private async Task<Scenario> CreateDraftAsync(
        bool completeProfile = true,
        int minimumAge = 18,
        DateOnly? dateOfBirth = null,
        bool blankAnswers = false)
    {
        var profile = _fx.SeedProfile(complete: completeProfile, dateOfBirth: dateOfBirth);
        _fx.Caller.UserId = profile.UserId;
        var product = _fx.SeedProduct(minimumAge: minimumAge);

        var draft = await _fx.Applications.CreateDraftAsync(
            new CreateApplicationRequest(product.Id, blankAnswers ? null : Answers), default);

        _draftId = draft.Id;
        return new Scenario(profile, product, draft.Id);
    }

    private async Task<Scenario> CreateSubmittedDraftAsync()
    {
        var scenario = await CreateDraftAsync();
        await _fx.Applications.SubmitAsync(scenario.DraftId, default);
        return scenario;
    }

    private Guid UseReviewer()
    {
        var reviewerId = Guid.NewGuid();
        _fx.Caller.UserId = reviewerId;
        return reviewerId;
    }
}
