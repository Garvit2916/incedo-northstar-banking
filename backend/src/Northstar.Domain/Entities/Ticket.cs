using Northstar.Domain.Common;

namespace Northstar.Domain.Entities;

/// <summary>
/// A customer support case worked by support agents. Customer references are
/// decoupled from the Identity user id so that synthetic demonstration data can
/// exist without real accounts.
/// </summary>
public class Ticket : AuditableEntity
{
    public string Reference { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public TicketCategory Category { get; set; }

    public TicketPriority Priority { get; set; } = TicketPriority.Normal;

    public TicketStatus Status { get; private set; } = TicketStatus.New;

    public Guid? CustomerUserId { get; set; }

    public Guid? RelatedApplicationId { get; set; }

    public Guid? AssignedAgentId { get; set; }

    public DateTimeOffset? SlaDueAtUtc { get; set; }

    public DateTimeOffset? ResolvedAtUtc { get; private set; }

    public SentimentLabel? LatestSentiment { get; private set; }

    public DateTimeOffset? SentimentAnalyzedAtUtc { get; private set; }

    public ICollection<TicketMessage> Messages { get; set; } = new List<TicketMessage>();

    public ICollection<TicketAssignment> Assignments { get; set; } = new List<TicketAssignment>();

    public void ChangeStatus(TicketStatus to, DateTimeOffset now)
    {
        if (Status == to)
        {
            return;
        }

        Status = to;
        ResolvedAtUtc = to is TicketStatus.Resolved or TicketStatus.Closed ? now : null;
    }

    public void RecordSentiment(SentimentLabel label, DateTimeOffset now)
    {
        LatestSentiment = label;
        SentimentAnalyzedAtUtc = now;
    }

    public void AssignTo(Guid agentUserId, Guid assignedByUserId, DateTimeOffset now)
    {
        foreach (var current in Assignments.Where(a => a.UnassignedAtUtc is null))
        {
            current.UnassignedAtUtc = now;
        }

        Assignments.Add(new TicketAssignment
        {
            TicketId = Id,
            AgentUserId = agentUserId,
            AssignedByUserId = assignedByUserId,
            AssignedAtUtc = now
        });

        AssignedAgentId = agentUserId;

        if (Status == TicketStatus.New)
        {
            ChangeStatus(TicketStatus.Open, now);
        }
    }
}
