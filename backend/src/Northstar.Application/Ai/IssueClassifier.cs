using Northstar.Domain;

namespace Northstar.Application.Ai;

/// <summary>Assistive, deterministic classification of a support conversation.</summary>
public sealed record IssueClassification(
    string SuggestedCategory,
    string IssueType,
    double Confidence,
    IReadOnlyList<string> Signals,
    string Explanation);

/// <summary>
/// Classifies untrusted ticket text into a banking support category. This is a
/// retrieval aid, not an automated decision: the original ticket category is
/// never overwritten and the result is always shown with its matched signals.
/// </summary>
public static class IssueClassifier
{
    private sealed record Rule(
        TicketCategory Category,
        string IssueType,
        string[] Signals);

    private static readonly IReadOnlyList<Rule> Rules = new[]
    {
        new Rule(TicketCategory.UnauthorizedTransaction, "UnauthorizedTransaction",
            new[] { "unauthorized", "unauthorised", "fraud", "scam", "phishing", "did not make", "didn't make", "not mine", "someone else" }),
        new Rule(TicketCategory.CardBlockingReplacement, "CardBlockingReplacement",
            new[] { "lost card", "stolen card", "block my card", "block the card", "replace my card", "replacement card", "damaged card" }),
        new Rule(TicketCategory.CardTransactionDispute, "CardTransactionDispute",
            new[] { "dispute", "wrong charge", "duplicate charge", "incorrect amount", "unrecognised charge", "unrecognized charge", "chargeback" }),
        new Rule(TicketCategory.FailedUpiTransaction, "FailedUpiTransaction",
            new[] { "upi", "utr", "failed payment", "money deducted", "payment pending", "refund" }),
        new Rule(TicketCategory.BankTransferIssue, "BankTransferIssue",
            new[] { "neft", "rtgs", "imps", "bank transfer", "beneficiary", "transfer pending" }),
        new Rule(TicketCategory.CreditCardBilling, "CreditCardBilling",
            new[] { "credit card bill", "statement", "minimum due", "minimum payment", "total due", "billing cycle", "due date" }),
        new Rule(TicketCategory.KycVerification, "KycVerification",
            new[] { "kyc", "proof of identity", "proof of address", "identity verification", "document rejected", "document pending" }),
        new Rule(TicketCategory.PersonalLoan, "PersonalLoan",
            new[] { "personal loan", "emi", "loan application", "outstanding loan", "loan payment" }),
        new Rule(TicketCategory.FixedDeposit, "FixedDeposit",
            new[] { "fixed deposit", "fd ", "maturity", "deposit tenure" }),
        new Rule(TicketCategory.ApplicationDelay, "ApplicationDelay",
            new[] { "application status", "under review", "track application", "application reference", "still pending" }),
        new Rule(TicketCategory.AccountAccess, "AccountAccess",
            new[] { "locked out", "cannot sign in", "can't sign in", "password reset", "account access", "login" }),
        new Rule(TicketCategory.NotificationIssue, "NotificationIssue",
            new[] { "notification", "email", "sms", "alert" }),
        new Rule(TicketCategory.ProductInquiry, "ProductInquiry",
            new[] { "which product", "recommend", "compare products", "savings account", "credit card application" })
    };

    public static IssueClassification Classify(AiTicketContext context)
    {
        var text = string.Join(" ", new[] { context.Subject, context.Description }
            .Concat(context.Conversation.Select(m => m.Body)));
        var normalised = text.ToLowerInvariant();

        var best = Rules
            .Select(rule => new
            {
                Rule = rule,
                Signals = rule.Signals.Where(s => normalised.Contains(s)).ToArray()
            })
            .Where(x => x.Signals.Length > 0)
            .OrderByDescending(x => x.Signals.Length)
            .ThenByDescending(x => x.Rule.Signals.Length)
            .FirstOrDefault();

        var categoryBoost = best is not null &&
                            best.Rule.Category.ToString().Equals(context.Category, StringComparison.OrdinalIgnoreCase)
            ? 0.2
            : 0;

        if (best is null)
        {
            return new IssueClassification(
                context.Category,
                context.Category,
                0,
                Array.Empty<string>(),
                "No banking-specific signal was found; the recorded ticket category is retained.");
        }

        var confidence = Math.Min(0.95, 0.4 + (0.18 * best.Signals.Length) + categoryBoost);
        return new IssueClassification(
            best.Rule.Category.ToString(),
            best.Rule.IssueType,
            Math.Round(confidence, 2),
            best.Signals,
            $"Matched {best.Signals.Length} banking signal(s) in the subject, description or conversation.");
    }
}
