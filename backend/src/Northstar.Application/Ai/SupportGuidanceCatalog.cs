using Northstar.Domain;

namespace Northstar.Application.Ai;

/// <summary>
/// Approved, human-authored support guidance. Only these statements may be used
/// to ground a suggested response; the model is instructed not to invent policy
/// details, timelines, or promises beyond this material.
/// </summary>
public static class SupportGuidanceCatalog
{
    private static readonly string[] Common =
    {
        "Never ask a customer for their password, one-time passcode (OTP), PIN, or full card number.",
        "Do not confirm or quote interest rates, fees, or returns unless they appear in an approved product disclosure.",
        "If you are unsure, tell the customer you will follow up rather than guessing.",
        "Application decisions are made only by authorised reviewers; support agents cannot approve or reject applications."
    };

    private static readonly IReadOnlyDictionary<TicketCategory, string[]> ByCategory =
        new Dictionary<TicketCategory, string[]>
        {
            [TicketCategory.AccountAccess] = new[]
            {
                "Guide the customer through the approved account-recovery process.",
                "If the account may be compromised, treat it as a priority and escalate to the security team."
            },
            [TicketCategory.ApplicationDelay] = new[]
            {
                "Application review timelines depend on completeness of information and documents.",
                "Encourage the customer to check their application status using their reference number."
            },
            [TicketCategory.MissingDocuments] = new[]
            {
                "Accepted document types are PDF, JPG, and PNG up to the configured size limit.",
                "Explain which specific document type is outstanding before asking for a re-upload."
            },
            [TicketCategory.StatusEnquiry] = new[]
            {
                "Customers can track progress from the application tracking page after signing in.",
                "Provide the current recorded status; do not speculate about a future outcome."
            },
            [TicketCategory.NotificationIssue] = new[]
            {
                "Check the customer's contact preferences and confirm the email address we hold.",
                "Do not disclose internal delivery logs; describe outcomes in customer-friendly terms."
            },
            [TicketCategory.Other] = new[]
            {
                "Ask for the specific information you need to help.",
                "Summarise the issue back to the customer to confirm understanding."
            }
        };

    public static IReadOnlyList<string> For(TicketCategory category) =>
        Common.Concat(ByCategory.TryGetValue(category, out var lines) ? lines : Array.Empty<string>())
            .ToList();
}
