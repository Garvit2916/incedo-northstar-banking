using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>History of ticket assignments, retained for operational reporting.</summary>
public class TicketAssignment : AuditableEntity
{
    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid AgentUserId { get; set; }

    public Guid AssignedByUserId { get; set; }

    public DateTimeOffset AssignedAtUtc { get; set; }

    public DateTimeOffset? UnassignedAtUtc { get; set; }
}
