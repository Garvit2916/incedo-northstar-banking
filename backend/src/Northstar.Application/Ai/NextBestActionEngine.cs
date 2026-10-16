using Northstar.Domain;

namespace Northstar.Application.Ai;

public sealed record NextBestActionInput(
    TicketCategory Category,
    TicketStatus Status,
    TicketPriority Priority,
    int OpenAgeDays,
    bool LastMessageFromCustomer,
    AiApplicationContext? Application,
    SentimentLabel? Sentiment);

/// <summary>
/// Deterministic, explainable recommendation engine for support tickets.
///
/// This is the authoritative source of next-best actions. It never performs or
/// authorises a privileged action - it only suggests what a human agent should
/// consider doing next, and states the rule and evidence behind each suggestion.
/// Keeping it deterministic (rather than model-driven) is a deliberate control:
/// recommendations must be reproducible and auditable.
/// </summary>
public static class NextBestActionEngine
{
    private const int EscalationAgeThresholdDays = 5;

    public static IReadOnlyList<NextBestAction> Recommend(NextBestActionInput input)
    {
        var actions = new List<NextBestAction>();

        // Application-related rules, in priority order.
        if (input.Application is { HasOutstandingDocuments: true } app)
        {
            var pending = app.DocumentReviewStates.Count == 0
                ? "one or more required documents"
                : string.Join(", ", app.DocumentReviewStates);

            actions.Add(new NextBestAction(
                Action: "Ask the customer to submit the outstanding KYC document(s).",
                Reason: "The related application has documents that are not in an approved state.",
                Evidence: $"Rule: outstanding-documents. Application {app.Reference}, status '{app.Status}', pending: {pending}.",
                Priority: "High",
                RequiresHumanApproval: true));
        }

        if (input.Application is { Status: not null } statusApp)
        {
            switch (statusApp.Status)
            {
                case nameof(ApplicationStatus.AdditionalInformationRequired):
                    actions.Add(new NextBestAction(
                        Action: "Explain which additional information was requested and how to resubmit.",
                        Reason: "The application is waiting on the customer to provide more information.",
                        Evidence: $"Rule: additional-information. Application {statusApp.Reference}.",
                        Priority: "High",
                        RequiresHumanApproval: true));
                    break;

                case nameof(ApplicationStatus.Submitted):
                case nameof(ApplicationStatus.UnderReview):
                    actions.Add(new NextBestAction(
                        Action: "Confirm the application is in review and explain how to track it.",
                        Reason: "The application is progressing normally and requires no customer action.",
                        Evidence: $"Rule: in-review. Application {statusApp.Reference}, status '{statusApp.Status}'.",
                        Priority: "Normal",
                        RequiresHumanApproval: true));
                    break;

                case nameof(ApplicationStatus.Approved):
                case nameof(ApplicationStatus.Rejected):
                    actions.Add(new NextBestAction(
                        Action: "Confirm the recorded decision and the outcome to the customer.",
                        Reason: "A decision has been recorded on the application.",
                        Evidence: $"Rule: decision-recorded. Application {statusApp.Reference}, status '{statusApp.Status}'.",
                        Priority: "Normal",
                        RequiresHumanApproval: true));
                    break;
            }
        }

        // Category-driven rules.
        switch (input.Category)
        {
            case TicketCategory.MissingDocuments:
                actions.Add(new NextBestAction(
                    Action: "List the accepted document types and provide upload instructions.",
                    Reason: "The ticket is categorised as a missing-documents enquiry.",
                    Evidence: "Rule: category-missing-documents.",
                    Priority: "High",
                    RequiresHumanApproval: true));
                break;

            case TicketCategory.StatusEnquiry:
                actions.Add(new NextBestAction(
                    Action: "Share the approved 'how to track your application' help article.",
                    Reason: "The customer is asking for a status update.",
                    Evidence: "Rule: category-status-enquiry.",
                    Priority: "Normal",
                    RequiresHumanApproval: true));
                break;

            case TicketCategory.NotificationIssue:
                actions.Add(new NextBestAction(
                    Action: "Verify the notification delivery log and confirm the customer's contact preferences.",
                    Reason: "The ticket concerns notifications not being received.",
                    Evidence: "Rule: category-notification-issue. Do not disclose internal delivery metadata.",
                    Priority: "Normal",
                    RequiresHumanApproval: true));
                break;

            case TicketCategory.AccountAccess:
                actions.Add(new NextBestAction(
                    Action: "Follow the approved secure account-recovery process; never request passwords, OTPs, or PINs.",
                    Reason: "The ticket concerns account access.",
                    Evidence: "Rule: category-account-access / security-policy.",
                    Priority: "High",
                    RequiresHumanApproval: true));
                break;
        }

        // Ageing and escalation rules.
        if (input.OpenAgeDays >= EscalationAgeThresholdDays &&
            input.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            actions.Add(new NextBestAction(
                Action: "Escalate the ticket to an authorised team.",
                Reason: $"The ticket has been open for {input.OpenAgeDays} days without resolution.",
                Evidence: $"Rule: ageing. Threshold: {EscalationAgeThresholdDays} days, priority '{input.Priority}'.",
                Priority: "High",
                RequiresHumanApproval: true));
        }

        if (input.Sentiment == SentimentLabel.UrgentEscalation)
        {
            actions.Add(new NextBestAction(
                Action: "Escalate to an authorised team and prioritise the ticket.",
                Reason: "Sentiment analysis flagged potential fraud, unauthorised access, or formal complaint language.",
                Evidence: "Rule: sentiment-escalation. Sentiment is an assistive signal and requires human confirmation.",
                Priority: "High",
                RequiresHumanApproval: true));
        }

        if (input.LastMessageFromCustomer &&
            input.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            actions.Add(new NextBestAction(
                Action: "Acknowledge the customer's latest message and provide a clear next step.",
                Reason: "The most recent message is from the customer and is awaiting a reply.",
                Evidence: "Rule: awaiting-agent-reply.",
                Priority: input.Priority == TicketPriority.Urgent ? "High" : "Normal",
                RequiresHumanApproval: true));
        }

        // Fallback so the agent always has at least one actionable suggestion.
        if (actions.Count == 0)
        {
            actions.Add(new NextBestAction(
                Action: "Review the conversation and request clarification if any detail is missing.",
                Reason: "No specific business rule applied to this ticket.",
                Evidence: "Rule: default-clarification.",
                Priority: "Low",
                RequiresHumanApproval: true));
        }

        return actions
            .OrderByDescending(a => PriorityRank(a.Priority))
            .ToList();
    }

    private static int PriorityRank(string priority) => priority switch
    {
        "High" => 3,
        "Normal" => 2,
        "Low" => 1,
        _ => 0
    };
}
