using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// A single message on a ticket's conversation thread. Internal notes are never
/// returned to customers.
/// </summary>
public class TicketMessage : AuditableEntity
{
    public Guid TicketId { get; set; }

    public Ticket Ticket { get; set; } = null!;

    public Guid? AuthorUserId { get; set; }

    public MessageAuthorType AuthorType { get; set; }

    public string Body { get; set; } = string.Empty;

    /// <summary>When true, the message is visible only to internal staff.</summary>
    public bool IsInternalNote { get; set; }
}
