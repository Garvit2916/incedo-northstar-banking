using Microsoft.EntityFrameworkCore;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers ticket creation, messaging, assignment and status bookkeeping. The
/// multi-message test is also the regression harness for the append-only child
/// promotion logic in <c>AppDbContext</c>: before that fix, saving the second
/// message failed with an update-count concurrency exception.
/// </summary>
public sealed class TicketServiceTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Creating_a_ticket_persists_the_generated_reference()
    {
        var customerId = Guid.NewGuid();

        var ticket = await CreateTicketAsync(customerId);

        Assert.Equal("NS-TKT-T000001", ticket.Reference);
        Assert.Equal(TicketStatus.New, ticket.Status);
        Assert.Equal(customerId, ticket.CustomerUserId);
        Assert.Contains("ticket.created", _fx.Audit.Actions);

        var stored = await _fx.Db.Tickets.AsNoTracking().SingleAsync();
        Assert.Equal(ticket.Id, stored.Id);
    }

    [Fact]
    public async Task Adding_messages_persists_every_row_as_an_insert()
    {
        var ticket = await CreateTicketAsync();

        var first = await _fx.Tickets.AddMessageAsync(
            ticket.Id, new AddTicketMessageRequest("Thanks for getting in touch.", false), default);
        var second = await _fx.Tickets.AddMessageAsync(
            ticket.Id, new AddTicketMessageRequest("Passing this to the back office.", true), default);

        // Creation seeds one message from the ticket description.
        Assert.Equal(2, first.Messages.Count);
        Assert.Equal(3, second.Messages.Count);

        var rows = await _fx.Db.TicketMessages.AsNoTracking()
            .Where(m => m.TicketId == ticket.Id)
            .ToListAsync();

        Assert.Equal(3, rows.Count);
        var internalNote = Assert.Single(rows, r => r.IsInternalNote);
        Assert.Equal("Passing this to the back office.", internalNote.Body);
        Assert.All(rows, r => Assert.Equal(MessageAuthorType.Agent, r.AuthorType));
        Assert.All(rows, r => Assert.Equal(_fx.Clock.UtcNow, r.CreatedAtUtc));
        Assert.Equal(2, _fx.Audit.Actions.Count(a => a == "ticket.message.added"));
    }

    [Fact]
    public async Task Assigning_a_ticket_records_the_assignment_and_opens_it()
    {
        var ticket = await CreateTicketAsync();
        var agentId = RegisterAgent("agent.one@northstar.example");

        var detail = await _fx.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(agentId), default);

        Assert.Equal(agentId, detail.AssignedAgentId);
        Assert.Equal(TicketStatus.Open, detail.Status);

        var assignment = Assert.Single(await _fx.Db.TicketAssignments.AsNoTracking()
            .Where(a => a.TicketId == ticket.Id)
            .ToListAsync());
        Assert.Equal(agentId, assignment.AgentUserId);
        Assert.Equal(_fx.Clock.UtcNow, assignment.AssignedAtUtc);
        Assert.Null(assignment.UnassignedAtUtc);
        Assert.Contains("ticket.assigned", _fx.Audit.Actions);
    }

    [Fact]
    public async Task Assigning_to_an_unknown_agent_throws_not_found()
    {
        var ticket = await CreateTicketAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _fx.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(Guid.NewGuid()), default));

        Assert.Empty(await _fx.Db.TicketAssignments.AsNoTracking()
            .Where(a => a.TicketId == ticket.Id)
            .ToListAsync());
    }

    [Fact]
    public async Task Reassigning_closes_the_previous_assignment_at_the_reassignment_time()
    {
        var ticket = await CreateTicketAsync();
        var firstAgent = RegisterAgent("agent.one@northstar.example");
        var secondAgent = RegisterAgent("agent.two@northstar.example");

        await _fx.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(firstAgent), default);
        _fx.Clock.Advance(TimeSpan.FromMinutes(15));
        await _fx.Tickets.AssignAsync(ticket.Id, new AssignTicketRequest(secondAgent), default);

        var assignments = await _fx.Db.TicketAssignments.AsNoTracking()
            .Where(a => a.TicketId == ticket.Id)
            .OrderBy(a => a.AssignedAtUtc)
            .ToListAsync();

        Assert.Equal(2, assignments.Count);
        Assert.NotNull(assignments[0].UnassignedAtUtc);
        Assert.Equal<DateTimeOffset?>(_fx.Clock.UtcNow, assignments[0].UnassignedAtUtc);
        Assert.Null(assignments[1].UnassignedAtUtc);
        Assert.Equal(secondAgent, assignments[1].AgentUserId);
    }

    [Fact]
    public async Task Resolving_a_ticket_stamps_the_resolution_time_and_reopening_clears_it()
    {
        var ticket = await CreateTicketAsync();

        var resolved = await _fx.Tickets.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Resolved), default);

        Assert.Equal(TicketStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAtUtc);
        Assert.Contains("ticket.status.changed", _fx.Audit.Actions);

        var reopened = await _fx.Tickets.ChangeStatusAsync(
            ticket.Id, new ChangeTicketStatusRequest(TicketStatus.Open), default);

        Assert.Equal(TicketStatus.Open, reopened.Status);
        Assert.Null(reopened.ResolvedAtUtc);
    }

    [Fact]
    public async Task Unknown_tickets_are_reported_as_missing()
    {
        await Assert.ThrowsAsync<NotFoundException>(
            () => _fx.Tickets.GetAsync(Guid.NewGuid(), default));
        await Assert.ThrowsAsync<NotFoundException>(
            () => _fx.Tickets.ChangeStatusAsync(
                Guid.NewGuid(), new ChangeTicketStatusRequest(TicketStatus.Closed), default));
    }

    // ---------------------------------------------------------------- helpers

    private Guid RegisterAgent(string email)
    {
        var agentId = Guid.NewGuid();
        _fx.Users.Users[agentId] = email;
        return agentId;
    }

    private async Task<TicketSummaryResponse> CreateTicketAsync(Guid? customerId = null)
    {
        var userId = customerId ?? Guid.NewGuid();
        _fx.Caller.UserId = userId;

        return await _fx.Tickets.CreateAsync(
            new CreateTicketRequest(
                "Where is my application?",
                "I submitted last week and would like an update.",
                TicketCategory.StatusEnquiry,
                TicketPriority.Normal,
                userId,
                null),
            default);
    }
}
