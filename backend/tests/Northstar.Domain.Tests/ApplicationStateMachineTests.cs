using Northstar.Domain;
using Northstar.Domain.Workflow;

namespace Northstar.Domain.Tests;

/// <summary>
/// Locks the application lifecycle table. Every (from, to) pair is asserted so a
/// future change to a single transition cannot silently loosen or tighten the
/// workflow without failing here.
/// </summary>
public sealed class ApplicationStateMachineTests
{
    private static readonly HashSet<(ApplicationStatus From, ApplicationStatus To)> Allowed = new()
    {
        (ApplicationStatus.Draft, ApplicationStatus.Submitted),
        (ApplicationStatus.Draft, ApplicationStatus.Withdrawn),

        (ApplicationStatus.Submitted, ApplicationStatus.UnderReview),
        (ApplicationStatus.Submitted, ApplicationStatus.Withdrawn),

        (ApplicationStatus.UnderReview, ApplicationStatus.Approved),
        (ApplicationStatus.UnderReview, ApplicationStatus.Rejected),
        (ApplicationStatus.UnderReview, ApplicationStatus.AdditionalInformationRequired),

        (ApplicationStatus.AdditionalInformationRequired, ApplicationStatus.Submitted),
        (ApplicationStatus.AdditionalInformationRequired, ApplicationStatus.Withdrawn)
    };

    public static IEnumerable<object[]> EveryTransitionPair()
    {
        foreach (var from in Enum.GetValues<ApplicationStatus>())
        {
            foreach (var to in Enum.GetValues<ApplicationStatus>())
            {
                var expected = Allowed.Contains((from, to));
                yield return new object[] { from, to, expected };
            }
        }
    }

    [Theory]
    [MemberData(nameof(EveryTransitionPair))]
    public void CanTransition_is_exactly_the_lifecycle_table(
        ApplicationStatus from,
        ApplicationStatus to,
        bool expected)
    {
        Assert.Equal(expected, ApplicationStateMachine.CanTransition(from, to));
    }

    [Theory]
    [InlineData(ApplicationStatus.Approved)]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public void Terminal_states_have_no_outgoing_transitions(ApplicationStatus terminal)
    {
        Assert.True(ApplicationStateMachine.IsTerminal(terminal));
        Assert.Empty(ApplicationStateMachine.AllowedFrom(terminal));
    }

    [Theory]
    [InlineData(ApplicationStatus.Draft)]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.UnderReview)]
    [InlineData(ApplicationStatus.AdditionalInformationRequired)]
    public void Non_terminal_states_can_still_move(ApplicationStatus status)
    {
        Assert.False(ApplicationStateMachine.IsTerminal(status));
        Assert.NotEmpty(ApplicationStateMachine.AllowedFrom(status));
    }

    [Fact]
    public void Draft_can_only_be_submitted_or_withdrawn()
    {
        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn },
            ApplicationStateMachine.AllowedFrom(ApplicationStatus.Draft).OrderBy(s => s).ToArray());
    }

    [Fact]
    public void Review_decisions_require_an_active_review()
    {
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.Submitted, ApplicationStatus.Approved));
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.Draft, ApplicationStatus.Rejected));
    }

    [Fact]
    public void Self_transitions_are_not_permitted_for_in_progress_states()
    {
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.Draft, ApplicationStatus.Draft));
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.UnderReview, ApplicationStatus.UnderReview));
    }

    [Fact]
    public void Rejected_application_cannot_be_resubmitted_or_reopened()
    {
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.Rejected, ApplicationStatus.Submitted));
        Assert.False(ApplicationStateMachine.CanTransition(
            ApplicationStatus.Rejected, ApplicationStatus.UnderReview));
    }

    [Fact]
    public void EnsureTransition_allows_permitted_moves()
    {
        ApplicationStateMachine.EnsureTransition(
            ApplicationStatus.Submitted, ApplicationStatus.UnderReview);
    }

    [Fact]
    public void EnsureTransition_throws_a_domain_exception_for_a_forbidden_move()
    {
        var ex = Assert.Throws<DomainException>(() =>
            ApplicationStateMachine.EnsureTransition(
                ApplicationStatus.Submitted, ApplicationStatus.Rejected));

        Assert.Contains("'Submitted' -> 'Rejected'", ex.Message);
    }
}
