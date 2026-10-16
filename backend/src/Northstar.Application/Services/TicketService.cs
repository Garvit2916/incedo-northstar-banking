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
/// Support ticket management for agents and administrators: creation,
/// assignment, conversation history, and status handling.
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

    public async Task<TicketSummaryResponse> CreateAsync(CreateTicketRequest request, CancellationToken ct)
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
            AuthorType = MessageAuthorType.Agent,
            AuthorUserId = _currentUser.UserId,
            Body = request.Description.Trim(),
            IsInternalNote = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });

        _db.Tickets.Add(ticket);
        await _db.SaveChangesAsync(ct);

        await _audit.RecordAsync("ticket.created", "Ticket", ticket.Id.ToString(), new { ticket.Reference });
        return ticket.ToSummary(ticket.Messages.Count);
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

        var items = await tickets
            .OrderByDescending(t => t.UpdatedAtUtc)
            .Skip((page.NormalisedPage - 1) * page.NormalisedPageSize)
            .Take(page.NormalisedPageSize)
            .Select(t => new TicketSummaryResponse(
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
                t.Messages.Count))
            .ToListAsync(ct);

        return new PagedResult<TicketSummaryResponse>(items, page.NormalisedPage, page.NormalisedPageSize, total);
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

    private async Task<Ticket> RequireTicketAsync(Guid id, CancellationToken ct) =>
        await _db.Tickets
            .Include(t => t.Messages)
            .Include(t => t.Assignments)
            .FirstOrDefaultAsync(t => t.Id == id, ct)
        ?? throw new NotFoundException("Ticket was not found.");

    private Guid RequireUserId() =>
        _currentUser.UserId ?? throw new ForbiddenException("Authentication is required.");
}
