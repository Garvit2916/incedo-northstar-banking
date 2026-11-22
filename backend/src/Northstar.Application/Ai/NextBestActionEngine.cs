using Northstar.Domain;

namespace Northstar.Application.Ai;

public sealed record NextBestActionInput(
    TicketCategory Category,
    TicketStatus Status,
    TicketPriority Priority,
    int OpenAgeDays,
    bool LastMessageFromCustomer,
    AiApplicationContext? Application,
    SentimentLabel? Sentiment,
    string Subject = "",
    string Description = "",
    IReadOnlyList<string>? ConversationBody = null,
    IReadOnlyList<KnowledgeHit>? Knowledge = null,
    IssueClassification? Classification = null);

/// <summary>
/// Deterministic, explainable recommendation engine for support tickets.
///
/// This is the authoritative source of next-best actions. It never performs or
/// authorises a privileged action - it only suggests what a human agent should
/// consider doing next, and states the rule, evidence and approved knowledge
/// article behind each suggestion. Recommendations are reproducible and auditable.
/// </summary>
public static class NextBestActionEngine
{
    private const int EscalationAgeThresholdDays = 5;
    private const int MaxActions = 6;

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
                RequiresHumanApproval: true,
                ExpectedOutcome: "The customer submits the required documents so the application can continue.",
                ActionType: NextBestActionTypes.RequestDocuments));
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
                        RequiresHumanApproval: true,
                        ExpectedOutcome: "The customer understands what is missing and resubmits the requested information.",
                        ActionType: NextBestActionTypes.RequestDocuments));
                    break;

                case nameof(ApplicationStatus.Submitted):
                case nameof(ApplicationStatus.UnderReview):
                    actions.Add(new NextBestAction(
                        Action: "Confirm the application is in review and explain how to track it.",
                        Reason: "The application is progressing normally and requires no customer action.",
                        Evidence: $"Rule: in-review. Application {statusApp.Reference}, status '{statusApp.Status}'.",
                        Priority: "Normal",
                        RequiresHumanApproval: true,
                        ExpectedOutcome: "The customer understands the application is progressing and knows how to track it.",
                        ActionType: NextBestActionTypes.Reply));
                    break;

                case nameof(ApplicationStatus.Approved):
                case nameof(ApplicationStatus.Rejected):
                    actions.Add(new NextBestAction(
                        Action: "Confirm the recorded decision and the outcome to the customer.",
                        Reason: "A decision has been recorded on the application.",
                        Evidence: $"Rule: decision-recorded. Application {statusApp.Reference}, status '{statusApp.Status}'.",
                        Priority: "Normal",
                        RequiresHumanApproval: true,
                        ExpectedOutcome: "The customer receives the recorded decision and understands the next steps.",
                        ActionType: NextBestActionTypes.Reply));
                    break;
            }
        }

        // Category-driven rules. Banking-specific categories are grounded below by
        // retrieved knowledge articles; these rules cover the original workflow.
        switch (input.Category)
        {
            case TicketCategory.MissingDocuments:
                actions.Add(new NextBestAction(
                    Action: "List the accepted document types and provide upload instructions.",
                    Reason: "The ticket is categorised as a missing-documents enquiry.",
                    Evidence: "Rule: category-missing-documents.",
                    Priority: "High",
                    RequiresHumanApproval: true,
                    ExpectedOutcome: "The customer knows which documents are accepted and how to upload them.",
                    ActionType: NextBestActionTypes.RequestDocuments));
                break;

            case TicketCategory.StatusEnquiry:
                actions.Add(new NextBestAction(
                    Action: "Share the approved 'how to track your application' help article.",
                    Reason: "The customer is asking for a status update.",
                    Evidence: "Rule: category-status-enquiry.",
                    Priority: "Normal",
                    RequiresHumanApproval: true,
                    ExpectedOutcome: "The customer can track the application without needing another status update.",
                    ActionType: NextBestActionTypes.Reply));
                break;

            case TicketCategory.NotificationIssue:
                actions.Add(new NextBestAction(
                    Action: "Verify the notification delivery log and confirm the customer's contact preferences.",
                    Reason: "The ticket concerns notifications not being received.",
                    Evidence: "Rule: category-notification-issue. Do not disclose internal delivery metadata.",
                    Priority: "Normal",
                    RequiresHumanApproval: true,
                    ExpectedOutcome: "Delivery is verified and the customer's contact preferences are confirmed.",
                    ActionType: NextBestActionTypes.FollowUp));
                break;

            case TicketCategory.AccountAccess:
                actions.Add(new NextBestAction(
                    Action: "Follow the approved secure account-recovery process; never request passwords, OTPs, or PINs.",
                    Reason: "The ticket concerns account access.",
                    Evidence: "Rule: category-account-access / security-policy.",
                    Priority: "High",
                    RequiresHumanApproval: true,
                    ExpectedOutcome: "Identity is verified through the approved process and access is restored safely.",
                    ActionType: NextBestActionTypes.VerifyIdentity));
                break;
        }

        // Assistive issue classification: the suggested category is not written back
        // to the ticket, but it helps an agent notice a mismatch.
        if (input.Classification is { Confidence: >= 0.4 } classification &&
            !classification.SuggestedCategory.Equals(input.Category.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            actions.Add(new NextBestAction(
                Action: $"Confirm whether this is a '{Humanise(classification.SuggestedCategory)}' issue before replying.",
                Reason: "The description and conversation contain banking-specific signals.",
                Evidence: $"Rule: issue-classification. Suggested '{classification.SuggestedCategory}' " +
                          $"with {classification.Confidence:0.00} confidence; signals: {string.Join(", ", classification.Signals)}.",
                Priority: classification.SuggestedCategory is
                    nameof(TicketCategory.UnauthorizedTransaction) or
                    nameof(TicketCategory.CardBlockingReplacement) or
                    nameof(TicketCategory.KycVerification)
                    ? "High"
                    : "Normal",
                RequiresHumanApproval: true,
                ExpectedOutcome: "The agent works from the correct issue type before drafting a response.",
                ActionType: NextBestActionTypes.Clarify)
            {
                Title = "Confirm the classified issue",
                Explanation = "The assistive classifier found a different banking issue than the recorded category.",
                Confidence = classification.Confidence
            });
        }

        // Knowledge-grounded actions. Each recommendation cites an approved demo
        // article and a safe workflow that still requires human confirmation.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var hit in (input.Knowledge ?? Array.Empty<KnowledgeHit>()).Take(3))
        {
            if (!seen.Add(hit.ArticleCode))
            {
                continue;
            }

            actions.Add(GroundedAction(hit));
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
                RequiresHumanApproval: true,
                ExpectedOutcome: "The ticket is owned by an authorised team with the context needed to resolve it.",
                ActionType: NextBestActionTypes.Escalate));
        }

        if (input.Sentiment == SentimentLabel.UrgentEscalation)
        {
            actions.Add(new NextBestAction(
                Action: "Escalate to an authorised team and prioritise the ticket.",
                Reason: "Sentiment analysis flagged potential fraud, unauthorised access, or formal complaint language.",
                Evidence: "Rule: sentiment-escalation. Sentiment is an assistive signal and requires human confirmation.",
                Priority: "High",
                RequiresHumanApproval: true,
                ExpectedOutcome: "A high-risk or vulnerable customer is routed to an authorised specialist quickly.",
                ActionType: NextBestActionTypes.Escalate));
        }

        if (input.LastMessageFromCustomer &&
            input.Status is not (TicketStatus.Resolved or TicketStatus.Closed))
        {
            actions.Add(new NextBestAction(
                Action: "Acknowledge the customer's latest message and provide a clear next step.",
                Reason: "The most recent message is from the customer and is awaiting a reply.",
                Evidence: "Rule: awaiting-agent-reply.",
                Priority: input.Priority == TicketPriority.Urgent ? "High" : "Normal",
                RequiresHumanApproval: true,
                ExpectedOutcome: "The customer receives an acknowledgement and a clear next step.",
                ActionType: NextBestActionTypes.Reply));
        }

        // Fallback so the agent always has at least one actionable suggestion.
        if (actions.Count == 0)
        {
            actions.Add(new NextBestAction(
                Action: "Review the conversation and request clarification if any detail is missing.",
                Reason: "No specific business rule applied to this ticket.",
                Evidence: "Rule: default-clarification.",
                Priority: "Low",
                RequiresHumanApproval: true,
                ExpectedOutcome: "Any missing detail is identified before the customer is asked for it.",
                ActionType: NextBestActionTypes.Clarify));
        }

        return actions
            .OrderByDescending(a => PriorityRank(a.Priority))
            .ThenByDescending(a => a.Confidence ?? 0)
            .Take(MaxActions)
            .ToList();
    }

    private static NextBestAction GroundedAction(KnowledgeHit hit)
    {
        var action = hit.ResolutionSteps.FirstOrDefault() ??
                     $"Review the approved '{hit.Title}' article before replying to the customer.";
        var priority = hit.Priority.Equals("High", StringComparison.OrdinalIgnoreCase)
            ? "High"
            : hit.Priority.Equals("Low", StringComparison.OrdinalIgnoreCase)
                ? "Low"
                : "Normal";

        return new NextBestAction(
            Action: action,
            Reason: hit.CustomerExplanation,
            Evidence: $"Knowledge: {hit.ArticleCode}. Match: {hit.MatchReason}. Score: {hit.Score:0.00}.",
            Priority: priority,
            RequiresHumanApproval: true,
            ExpectedOutcome: hit.CustomerExplanation,
            ActionType: ActionTypeFor(hit.IssueType))
        {
            Title = hit.Title,
            Explanation = hit.AgentGuidance,
            Sources = new[] { hit },
            SuggestedWorkflow = WorkflowFor(hit.IssueType),
            Confidence = Math.Min(0.95, hit.Score / 12d)
        };
    }

    private static string ActionTypeFor(string issueType) => issueType switch
    {
        "UnauthorizedTransaction" or "AccountAccess" => NextBestActionTypes.VerifyIdentity,
        "CardBlockingReplacement" or "CardTransactionDispute" or "FailedUpiTransaction" or "BankTransferIssue"
            => NextBestActionTypes.FollowUp,
        "KycVerification" or "MissingDocuments" => NextBestActionTypes.RequestDocuments,
        "PersonalLoan" or "ApplicationDelay" or "StatusEnquiry" => NextBestActionTypes.ReviewApplication,
        _ => NextBestActionTypes.Reply
    };

    private static string WorkflowFor(string issueType) => issueType switch
    {
        "UnauthorizedTransaction" => "fraud-review-confirmation",
        "CardBlockingReplacement" => "safe-card-block-confirmation",
        "CardTransactionDispute" => "demo-card-dispute-record",
        "CreditCardBilling" => "statement-explanation",
        "FailedUpiTransaction" => "upi-refund-review",
        "BankTransferIssue" => "payment-trace",
        "KycVerification" or "MissingDocuments" => "secure-kyc-upload",
        "PersonalLoan" => "loan-payment-review",
        "FixedDeposit" => "product-terms-review",
        "ApplicationDelay" or "StatusEnquiry" => "application-status-check",
        "ProductInquiry" => "product-comparison",
        _ => "agent-review"
    };

    private static string Humanise(string value)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < value.Length; i++)
        {
            if (i > 0 && char.IsUpper(value[i]))
            {
                builder.Append(' ');
            }

            builder.Append(i == 0 ? char.ToUpperInvariant(value[i]) : char.ToLowerInvariant(value[i]));
        }

        return builder.ToString();
    }

    private static int PriorityRank(string priority) => priority switch
    {
        "High" => 3,
        "Normal" => 2,
        "Low" => 1,
        _ => 0
    };
}
