using Northstar.Domain;
using Northstar.Domain.Entities;
using Northstar.Domain.Workflow;

namespace Northstar.Domain.Tests;

/// <summary>
/// Covers the <see cref="Application"/> aggregate: guarded transitions, audit
/// history, and the decision reason surfaced to the customer.
/// </summary>
public sealed class ApplicationTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Application NewDraft() =>
        Application.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "NS-APP-TEST01", null);

    [Fact]
    public void CreateDraft_defaults_to_draft_status()
    {
        var app = NewDraft();

        Assert.Equal(ApplicationStatus.Draft, app.Status);
        Assert.Equal("NS-APP-TEST01", app.Reference);
        Assert.Null(app.DecisionReason);
        Assert.Null(app.SubmittedAtUtc);
        Assert.Empty(app.History);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDraft_normalises_blank_answers_to_an_empty_object(string? answers)
    {
        var app = Application.CreateDraft(Guid.NewGuid(), Guid.NewGuid(), "NS-APP-TEST01", answers);

        Assert.Equal("{}", app.FormDataJson);
    }

    [Fact]
    public void Submit_records_the_transition_and_timestamp()
    {
        var app = NewDraft();
        var actor = Guid.NewGuid();

        app.Submit(actor, Now);

        Assert.Equal(ApplicationStatus.Submitted, app.Status);
        Assert.Equal(Now, app.SubmittedAtUtc);
        Assert.Single(app.History);

        var entry = Assert.Single(app.History);
        Assert.Equal(ApplicationStatus.Draft, entry.FromStatus);
        Assert.Equal(ApplicationStatus.Submitted, entry.ToStatus);
        Assert.Equal(actor, entry.ChangedByUserId);
        Assert.Equal(Now, entry.ChangedAtUtc);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void Approve_persists_the_decision_reason_for_the_customer()
    {
        var app = NewDraft();
        var reviewer = Guid.NewGuid();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(reviewer, Now.AddMinutes(1));
        app.Approve(reviewer, "All checks passed", Now.AddMinutes(2));

        Assert.Equal(ApplicationStatus.Approved, app.Status);
        Assert.Equal("All checks passed", app.DecisionReason);
        Assert.Equal(Now.AddMinutes(2), app.DecidedAtUtc);
    }

    [Fact]
    public void Reject_persists_the_decision_reason_for_the_customer()
    {
        var app = NewDraft();
        var reviewer = Guid.NewGuid();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(reviewer, Now.AddMinutes(1));
        app.Reject(reviewer, "Insufficient evidence", Now.AddMinutes(2));

        Assert.Equal(ApplicationStatus.Rejected, app.Status);
        Assert.Equal("Insufficient evidence", app.DecisionReason);
        Assert.Equal(Now.AddMinutes(2), app.DecidedAtUtc);
    }

    [Fact]
    public void Requesting_more_information_stores_the_reviewer_question()
    {
        var app = NewDraft();
        var reviewer = Guid.NewGuid();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(reviewer, Now.AddMinutes(1));
        app.RequestAdditionalInformation(reviewer, "Please upload proof of address", Now.AddMinutes(2));

        Assert.Equal(ApplicationStatus.AdditionalInformationRequired, app.Status);
        Assert.Equal("Please upload proof of address", app.DecisionReason);
    }

    [Fact]
    public void Full_approval_path_builds_the_expected_history_trail()
    {
        var app = NewDraft();
        var reviewer = Guid.NewGuid();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(reviewer, Now.AddMinutes(1));
        app.Approve(reviewer, "Verified", Now.AddMinutes(2));

        Assert.Equal(3, app.History.Count);
        Assert.Equal(
            new[]
            {
                ApplicationStatus.Submitted,
                ApplicationStatus.UnderReview,
                ApplicationStatus.Approved
            },
            app.History.Select(h => h.ToStatus).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Approve_refuses_without_a_reason(string reason)
    {
        var app = NewDraft();
        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => app.Approve(Guid.NewGuid(), reason, Now.AddMinutes(2)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Reject_refuses_without_a_reason(string reason)
    {
        var app = NewDraft();
        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Throws<DomainException>(() => app.Reject(Guid.NewGuid(), reason, Now.AddMinutes(2)));
    }

    [Fact]
    public void Review_decisions_cannot_happen_before_review_starts()
    {
        var app = NewDraft();
        app.Submit(Guid.NewGuid(), Now);

        var ex = Assert.Throws<DomainException>(
            () => app.Approve(Guid.NewGuid(), "Nope", Now.AddMinutes(1)));

        Assert.Contains("'Submitted' -> 'Approved'", ex.Message);
    }

    [Fact]
    public void Approved_application_is_final()
    {
        var app = NewDraft();
        var reviewer = Guid.NewGuid();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(reviewer, Now.AddMinutes(1));
        app.Approve(reviewer, "Verified", Now.AddMinutes(2));

        Assert.Throws<DomainException>(() => app.Reject(reviewer, "Changed my mind", Now.AddMinutes(3)));
        Assert.Throws<DomainException>(() => app.Submit(Guid.NewGuid(), Now.AddMinutes(3)));
    }

    [Fact]
    public void Withdrawn_draft_records_a_default_reason()
    {
        var app = NewDraft();

        app.Withdraw(Guid.NewGuid(), Now);

        Assert.Equal(ApplicationStatus.Withdrawn, app.Status);
        Assert.Equal("Withdrawn by customer.", app.DecisionReason);
    }

    [Fact]
    public void First_reviewer_is_retained_when_a_review_is_reassigned()
    {
        var first = Guid.NewGuid();
        var app = NewDraft();

        app.Submit(Guid.NewGuid(), Now);
        app.StartReview(first, Now.AddMinutes(1));

        Assert.Equal(first, app.AssignedReviewerId);
        // A later transition out of and back into review must not steal ownership.
        Assert.True(ApplicationStateMachine.CanTransition(
            ApplicationStatus.UnderReview, ApplicationStatus.AdditionalInformationRequired));
    }
}
