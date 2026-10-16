using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Domain.Tests;

/// <summary>Covers ticket lifecycle bookkeeping and assignment history.</summary>
public sealed class TicketTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static Ticket NewTicket() => new()
    {
        Reference = "NS-TKT-9999",
        Subject = "Where is my application?",
        Description = "Customer is asking for an update.",
        Category = TicketCategory.StatusEnquiry
    };

    [Fact]
    public void New_ticket_starts_in_new_status()
    {
        var ticket = NewTicket();

        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Null(ticket.ResolvedAtUtc);
        Assert.Null(ticket.AssignedAgentId);
    }

    [Fact]
    public void Resolving_a_ticket_stamps_the_resolution_time()
    {
        var ticket = NewTicket();

        ticket.ChangeStatus(TicketStatus.Resolved, Now);

        Assert.Equal(TicketStatus.Resolved, ticket.Status);
        Assert.Equal(Now, ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Reissuing_the_same_status_does_not_move_the_resolution_time()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Resolved, Now);

        ticket.ChangeStatus(TicketStatus.Resolved, Now.AddHours(1));

        Assert.Equal(Now, ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Reopening_a_resolved_ticket_clears_the_resolution_time()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Resolved, Now);

        ticket.ChangeStatus(TicketStatus.Open, Now.AddMinutes(5));

        Assert.Null(ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Non_terminal_statuses_never_carry_a_resolution_time()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Resolved, Now);

        ticket.ChangeStatus(TicketStatus.PendingCustomer, Now.AddMinutes(1));

        Assert.Null(ticket.ResolvedAtUtc);
    }

    [Fact]
    public void Assigning_a_new_ticket_opens_it_and_records_the_assignment()
    {
        var ticket = NewTicket();
        var agent = Guid.NewGuid();
        var actor = Guid.NewGuid();

        ticket.AssignTo(agent, actor, Now);

        Assert.Equal(TicketStatus.Open, ticket.Status);
        Assert.Equal(agent, ticket.AssignedAgentId);

        var assignment = Assert.Single(ticket.Assignments);
        Assert.Equal(agent, assignment.AgentUserId);
        Assert.Equal(actor, assignment.AssignedByUserId);
        Assert.Null(assignment.UnassignedAtUtc);
    }

    [Fact]
    public void Reassigning_closes_the_previous_active_assignment_only()
    {
        var ticket = NewTicket();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var actor = Guid.NewGuid();

        ticket.AssignTo(first, actor, Now);
        ticket.AssignTo(second, actor, Now.AddMinutes(10));

        Assert.Equal(2, ticket.Assignments.Count);

        // The first assignment ends when the reassignment happens, not when it began.
        Assert.Equal(
            Now.AddMinutes(10),
            ticket.Assignments.Single(a => a.AgentUserId == first).UnassignedAtUtc);
        Assert.Null(ticket.Assignments.Single(a => a.AgentUserId == second).UnassignedAtUtc);
        Assert.Equal(second, ticket.AssignedAgentId);
    }

    [Fact]
    public void Assigning_an_already_open_ticket_keeps_its_status()
    {
        var ticket = NewTicket();
        ticket.ChangeStatus(TicketStatus.Escalated, Now);

        ticket.AssignTo(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(1));

        Assert.Equal(TicketStatus.Escalated, ticket.Status);
    }
}
