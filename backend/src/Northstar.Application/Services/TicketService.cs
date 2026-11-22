using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Application.Mapping;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Support ticket management: agent/admin assignment, conversation and status
/// handling, plus scoped customer self-service creation and tracking.
/// </summary>
public sealed class TicketService
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IAuditWriter _audit;
    private readonly IReferenceGenerator _references;
    private readonly IUserDirectory _users;
    private readonly IValidator<CreateTicketRequest> _createValidator;
    private readonly IValidator<AddTicketMessageRequest> _messageValidator;
    private readonly IValidator<AssignTicketRequest> _assignValidator;

    public TicketService(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IAuditWriter audit,
        IReferenceGenerator references,
        IUserDirectory users,
        IValidator<CreateTicketRequest> createValidator,
        IValidator<AddTicketMessageRequest> messageValidator,
        IValidator<AssignTicketRequest> assignValidator)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _audit = audit;
        _references = references;
        _users = users;
        _createValidator = createValidator;
        _messageValidator = messageValidator;
        _assignValidator = assignValidator;
    }

    /// <summary>Agent/admin creation. The customer may be supplied explicitly.</summary>
    public Task<TicketSummaryResponse> CreateAsync(CreateTicketRequest request, CancellationToken ct) =>
        CreateCoreAsync(request, MessageAuthorType.Agent, ct);

    /// <summary>
    /// Customer self-service creation. Ownership always comes from the current
    /// principal, and a related application must belong to that customer.
    /// </summary>
    public async Task<CustomerTicketSummaryResponse> CreateForCustomerAsync(
        CreateCustomerTicketRequest request,
        CancellationToken ct)
    {
        var customerUserId = RequireUserId();
        if (request.RelatedApplicationId is { } applicationId &&
            !await _db.Applications.AnyAsync(
                a => a.Id == applicationId && a.CustomerProfile!.UserId == customerUserId, ct))
        {
            // Do not reveal whether another customer's application exists.
            throw new NotFoundException("Related application was not found.");
        }

        // CreateCoreAsync validates the mapped request, so the customer request
        // is not validated twice.
        var created = await CreateCoreAsync(
            new CreateTicketRequest(
                request.Subject,
                request.Description,
                request.Category,
                request.Priority,
                customerUserId,
                request.RelatedApplicationId),
            MessageAuthorType.Customer,
            ct);

        return created.ToCustomerSummary();
    }

    private async Task<TicketSummaryResponse> CreateCoreAsync(
        CreateTicketRequest request,
        MessageAuthorType authorType,
        CancellationToken ct)
    {
        await _createValidator.ValidateAndThrowAsync(request, ct);

        var now = _clock.UtcNow;
        var ticket = new Ticket
        {
            Reference = _references.NextTicketReference(),
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            Category = request.Category,
            Priority = request.Priority,
            CustomerUserId = request.CustomerUserId,
            RelatedApplicationId = request.RelatedApplicationId,
            SlaDueAtUtc = now.AddHours(request.Priority switch
            {
                TicketPriority.Urgent => 4,
                TicketPriority.High => 8,
                TicketPriority.Normal => 24,
                _ => 48
            }),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        ticket.Messages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorType = authorType,
            AuthorUserId = _currentUser.UserId,
            Body = request.Description.Trim(),
            IsInternalNote = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync(
            "ticket.created",
            "Ticket",
            ticket.Id.ToString(),
            new { ticket.Reference, submittedBy = authorType.ToString() });
        return ticket.ToSummary(ticket.Messages.Count);
    }

    /// <summary>
    /// Lists only the caller's tickets, counting public messages only and hiding
    /// internal sentiment analytics.
    /// </summary>
    public async Task<PagedResult<CustomerTicketSummaryResponse>> ListMineAsync(PageQuery page, CancellationToken ct)
    {
        var userId = RequireUserId();
        var tickets = _db.Tickets.AsNoTracking().Where(t => t.CustomerUserId == userId);

        var total = await tickets.CountAsync(ct);
        var paged = tickets
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize);

        var items = await ProjectCustomerSummaries(paged).ToListAsync(ct);

        return new PagedResult<CustomerTicketSummaryResponse>(
            items, page.NormalisedPage, page.NormalisedPageSize, total);
    }

    public async Task<PagedResult<TicketSummaryResponse>> ListAsync(TicketQuery query, CancellationToken ct)
    {
        var tickets = _db.Tickets.AsNoTracking().AsQueryable();

        if (query.Status is not null)
        {
            tickets = tickets.Where(t => t.Status == query.Status);
        }

        if (query.Category is not null)
        {
            tickets = tickets.Where(t => t.Category == query.Category);
        }

        if (query.Priority is not null)
        {
            tickets = tickets.Where(t => t.Priority == query.Priority);
        }

        if (query.AssignedAgentId is not null)
        {
            tickets = tickets.Where(t => t.AssignedAgentId == query.AssignedAgentId);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            tickets = tickets.Where(t => t.Subject.Contains(term) || t.Reference.Contains(term));
        }

        var page = new PageQuery(query.Page, query.PageSize);
        var total = await tickets.CountAsync(ct);

        var paged = tickets
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize);

        var items = await ProjectSummaries(paged, publicMessagesOnly: false).ToListAsync(ct);

        return new PagedResult<TicketSummaryResponse>(items, page.NormalisedPage, page.NormalisedPageSize, total);
    }

    public async Task<TicketOverviewResponse> GetOverviewAsync(CancellationToken ct)
    {
        var userId = RequireUserId();
        var startOfDay = new DateTimeOffset(_clock.UtcNow.UtcDateTime.Date, TimeSpan.Zero);

        // One grouped query keeps the overview to a single database round trip.
        var counts = await _db.Tickets
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Open = g.Count(t => t.Status != TicketStatus.Resolved && t.Status != TicketStatus.Closed),
                Unassigned = g.Count(t => t.Status != TicketStatus.Resolved &&
                                          t.Status != TicketStatus.Closed &&
                                          t.AssignedAgentId == null),
                Urgent = g.Count(t => t.Status != TicketStatus.Resolved &&
                                      t.Status != TicketStatus.Closed &&
                                      t.Priority == TicketPriority.Urgent),
                Escalated = g.Count(t => t.Status == TicketStatus.Escalated),
                AwaitingCustomer = g.Count(t => t.Status == TicketStatus.PendingCustomer),
                ResolvedToday = g.Count(t => t.ResolvedAtUtc >= startOfDay),
                AssignedToMe = g.Count(t => t.Status != TicketStatus.Resolved &&
                                            t.Status != TicketStatus.Closed &&
                                            t.AssignedAgentId == userId),
                NegativeSentiment = g.Count(t => t.LatestSentiment == SentimentLabel.Negative),
                UrgentEscalationSentiment = g.Count(t => t.LatestSentiment == SentimentLabel.UrgentEscalation),
                AnalyzedSentiment = g.Count(t => t.LatestSentiment != null)
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new TicketOverviewResponse(0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
            : new TicketOverviewResponse(
                counts.Open,
                counts.Unassigned,
                counts.Urgent,
                counts.Escalated,
                counts.AwaitingCustomer,
                counts.ResolvedToday,
                counts.AssignedToMe,
                counts.NegativeSentiment,
                counts.UrgentEscalationSentiment,
                counts.AnalyzedSentiment);
    }

    public async Task<TicketDetailResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw new NotFoundException("Ticket was not found.");

        return ticket.ToDetail();
    }

    /// <summary>
    /// Customer detail view. Internal notes are filtered by the mapping layer and
    /// other customers' tickets are indistinguishable from missing ones.
    /// </summary>
    public async Task<CustomerTicketDetailResponse> GetMineAsync(Guid id, CancellationToken ct)
    {
        var userId = RequireUserId();
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.Id == id && t.CustomerUserId == userId, ct)
            ?? throw new NotFoundException("Ticket was not found.");

        return ticket.ToCustomerDetail();
    }

    public async Task<TicketDetailResponse> AddMessageAsync(
        Guid id,
        AddTicketMessageRequest request,
        CancellationToken ct)
    {
        await _messageValidator.ValidateAndThrowAsync(request, ct);

        var ticket = await RequireTicketAsync(id, ct);
        var now = _clock.UtcNow;

        ticket.Messages.Add(new TicketMessage
        {
            TicketId = ticket.Id,
            AuthorType = MessageAuthorType.Agent,
            AuthorUserId = _currentUser.UserId,
            Body = request.Body.Trim(),
            IsInternalNote = request.IsInternalNote,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        ticket.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync("ticket.message.added", "Ticket", ticket.Id.ToString(), new { request.IsInternalNote });
        return await GetAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailResponse> ChangeStatusAsync(
        Guid id,
        ChangeTicketStatusRequest request,
        CancellationToken ct)
    {
        var ticket = await RequireTicketAsync(id, ct);
        var now = _clock.UtcNow;

        ticket.ChangeStatus(request.Status, now);
        ticket.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("ticket.status.changed", "Ticket", ticket.Id.ToString(), new { request.Status });

        return await GetAsync(ticket.Id, ct);
    }

    public async Task<TicketDetailResponse> AssignAsync(Guid id, AssignTicketRequest request, CancellationToken ct)
    {
        await _assignValidator.ValidateAndThrowAsync(request, ct);

        if (!await _users.UserExistsAsync(request.AgentUserId, ct))
        {
            throw new NotFoundException("The specified agent was not found.");
        }

        var ticket = await RequireTicketAsync(id, ct);
        var now = _clock.UtcNow;

        ticket.AssignTo(request.AgentUserId, RequireUserId(), now);
        ticket.UpdatedAtUtc = now;

        await _db.SaveChangesAsync(ct);
        await _audit.RecordAsync("ticket.assigned", "Ticket", ticket.Id.ToString(), new { request.AgentUserId });

        return await GetAsync(ticket.Id, ct);
    }

    private static IQueryable<TicketSummaryResponse> ProjectSummaries(
        IQueryable<Ticket> tickets,
        bool publicMessagesOnly) =>
        tickets.Select(t => new TicketSummaryResponse(
            t.Id,
            t.Reference,
            t.Subject,
            t.Category,
            t.Priority,
            t.Status,
            t.CustomerUserId,
            t.AssignedAgentId,
            t.RelatedApplicationId,
            t.SlaDueAtUtc,
            t.ResolvedAtUtc,
            t.CreatedAtUtc,
            t.UpdatedAtUtc,
            publicMessagesOnly ? t.Messages.Count(m => !m.IsInternalNote) : t.Messages.Count,
            t.LatestSentiment,
            t.SentimentAnalyzedAtUtc));

    private static IQueryable<CustomerTicketSummaryResponse> ProjectCustomerSummaries(
        IQueryable<Ticket> tickets) =>
        tickets.Select(t => new CustomerTicketSummaryResponse(
            t.Id,
            t.Reference,
            t.Subject,
            t.Category,
            t.Priority,
            t.Status,
            t.CustomerUserId,
            t.AssignedAgentId,
            t.RelatedApplicationId,
            t.SlaDueAtUtc,
            t.ResolvedAtUtc,
            t.CreatedAtUtc,
            t.UpdatedAtUtc,
            t.Messages.Count(m => !m.IsInternalNote)));

    private async Task<Ticket> RequireTicketAsync(Guid id, CancellationToken ct) =>
        await _db.Tickets
            .Include(t => t.Messages)
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Ticket was not found.");

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
