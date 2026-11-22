using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers customer-submitted tickets: server-derived ownership, related-application
/// isolation, scoped reads, and exclusion of internal notes from customer responses.
/// </summary>
public sealed class CustomerTicketTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Customer_ticket_is_owned_by_caller_and_opens_with_customer_message()
    {
        var customer = _fx.SeedUser();
        _fx.Caller.UserId = customer.Id;

        var created = await _fx.Tickets.CreateForCustomerAsync(
            new CreateCustomerTicketRequest(
                "Card charge I do not recognise",
                "I do not recognise a £40 charge from yesterday.",
                TicketCategory.CardTransactionDispute,
                TicketPriority.High,
                null),
            default);

        Assert.Equal(customer.Id, created.CustomerUserId);
        Assert.Contains("ticket.created", _fx.Audit.Actions);

        var detail = await _fx.Tickets.GetMineAsync(created.Id, default);
        var opening = Assert.Single(detail.Messages);
        Assert.Equal(MessageAuthorType.Customer, opening.AuthorType);
        Assert.False(opening.IsInternalNote);
        Assert.Contains("£40", opening.Body);
    }

    [Fact]
    public async Task Customer_cannot_attach_another_customers_application()
    {
        var owner = _fx.SeedUser();
        var other = _fx.SeedUser();
        var profile = _fx.SeedProfile(owner.Id);
        var product = _fx.SeedProduct();
        var application = Northstar.Domain.Entities.Application.CreateDraft(
            profile.Id, product.Id, "NS-APP-PRIVATE", null);
        _fx.Db.Applications.Add(application);
        await _fx.Db.SaveChangesAsync(default);

        _fx.Caller.UserId = other.Id;

        await Assert.ThrowsAsync<NotFoundException>(() => _fx.Tickets.CreateForCustomerAsync(
            new CreateCustomerTicketRequest(
                "Application question",
                "Please explain the status.",
                TicketCategory.StatusEnquiry,
                TicketPriority.Normal,
                application.Id),
            default));

        Assert.Equal(0, await _fx.Db.Tickets.CountAsync());
    }

    [Fact]
    public async Task Customer_list_and_detail_are_scoped_to_the_caller()
    {
        var first = _fx.SeedUser();
        var second = _fx.SeedUser();

        _fx.Caller.UserId = first.Id;
        var mine = await CreateTicketAsync("My support request");

        _fx.Caller.UserId = second.Id;
        var theirs = await CreateTicketAsync("Another customer request");

        var list = await _fx.Tickets.ListMineAsync(new PageQuery(1, 20), default);
        Assert.Equal(1, list.TotalCount);
        Assert.Equal(theirs.Id, Assert.Single(list.Items).Id);
        Assert.DoesNotContain(mine.Id, list.Items.Select(t => t.Id));

        await Assert.ThrowsAsync<NotFoundException>(() => _fx.Tickets.GetMineAsync(mine.Id, default));
        Assert.Equal(theirs.Id, (await _fx.Tickets.GetMineAsync(theirs.Id, default)).Id);
    }

    [Fact]
    public async Task Customer_detail_hides_internal_notes_and_counts_public_messages_only()
    {
        var customer = _fx.SeedUser();
        var agent = _fx.SeedUser();

        _fx.Caller.UserId = customer.Id;
        var ticket = await CreateTicketAsync("Please check my account");

        _fx.Caller.UserId = agent.Id;
        await _fx.Tickets.AddMessageAsync(
            ticket.Id,
            new AddTicketMessageRequest("Internal: verify tier-2 queue", true),
            default);

        _fx.Caller.UserId = customer.Id;
        var detail = await _fx.Tickets.GetMineAsync(ticket.Id, default);
        Assert.DoesNotContain("Internal: verify tier-2 queue", detail.Messages.Select(m => m.Body));

        var list = await _fx.Tickets.ListMineAsync(new PageQuery(1, 20), default);
        Assert.Equal(1, Assert.Single(list.Items).MessageCount);
    }

    [Fact]
    public async Task Customer_ticket_validation_rejects_empty_subject()
    {
        var customer = _fx.SeedUser();
        _fx.Caller.UserId = customer.Id;

        await Assert.ThrowsAsync<ValidationException>(() => _fx.Tickets.CreateForCustomerAsync(
            new CreateCustomerTicketRequest(
                string.Empty,
                "A detailed description.",
                TicketCategory.Other,
                TicketPriority.Normal,
                null),
            default));
    }

    private Task<Northstar.Application.Contracts.CustomerTicketSummaryResponse> CreateTicketAsync(string subject) =>
        _fx.Tickets.CreateForCustomerAsync(
            new CreateCustomerTicketRequest(
                subject,
                "Please look into this for me.",
                TicketCategory.StatusEnquiry,
                TicketPriority.Normal,
                null),
            default);
}
