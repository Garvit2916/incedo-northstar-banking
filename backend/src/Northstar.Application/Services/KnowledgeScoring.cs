using System.Text.RegularExpressions;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>
/// Transparent relevance scoring for the demo knowledge base. The algorithm is
/// deliberately explainable: every match contributes a recorded reason so agents
/// can see why an article was retrieved. A future vector search can implement the
/// same <c>IKnowledgeRetrievalService</c> port without changing callers.
/// </summary>
internal static partial class KnowledgeScoring
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "a", "an", "and", "or", "but", "is", "are", "was", "were", "be", "been",
        "to", "of", "for", "in", "on", "at", "by", "with", "from", "my", "me", "i",
        "it", "this", "that", "please", "help", "have", "has", "had", "do", "does", "did",
        "can", "could", "would", "should", "not", "no", "you", "your", "we", "our", "they"
    };

    /// <summary>
    /// Ticket categories are workflow states; the knowledge catalogue uses broader
    /// subject categories. Map explicitly so the category signal is not silently lost.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> KnowledgeCategoryByTicketCategory =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(TicketCategory.AccountAccess)] = "Accounts",
            [nameof(TicketCategory.ApplicationDelay)] = "Applications",
            [nameof(TicketCategory.MissingDocuments)] = "KYC",
            [nameof(TicketCategory.StatusEnquiry)] = "Applications",
            [nameof(TicketCategory.NotificationIssue)] = "Operations",
            [nameof(TicketCategory.CardTransactionDispute)] = "Cards",
            [nameof(TicketCategory.UnauthorizedTransaction)] = "Security",
            [nameof(TicketCategory.CardBlockingReplacement)] = "Cards",
            [nameof(TicketCategory.CreditCardBilling)] = "Cards",
            [nameof(TicketCategory.FailedUpiTransaction)] = "Payments",
            [nameof(TicketCategory.BankTransferIssue)] = "Payments",
            [nameof(TicketCategory.KycVerification)] = "KYC",
            [nameof(TicketCategory.PersonalLoan)] = "Loans",
            [nameof(TicketCategory.FixedDeposit)] = "Deposits",
            [nameof(TicketCategory.ProductInquiry)] = "Products"
        };

    [GeneratedRegex(@"[a-z0-9]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return TokenRegex()
            .Matches(text)
            .Select(m => m.Value.ToLowerInvariant())
            .Where(t => t.Length > 2 && !StopWords.Contains(t))
            .Distinct()
            .ToList();
    }

    public static (double Score, string Reason) ScoreTicket(
        KnowledgeArticle article,
        string ticketCategory,
        string subject,
        string description,
        IReadOnlyList<string> conversation,
        string? product)
    {
        var text = string.Join(" ", new[] { subject, description }.Concat(conversation));
        var terms = Tokenize(text);
        var category = KnowledgeCategoryByTicketCategory.TryGetValue(ticketCategory, out var mapped)
            ? mapped
            : null;
        return Score(article, terms, category, product, ticketCategory);
    }

    public static (double Score, string Reason) ScoreSearch(
        KnowledgeArticle article,
        string search,
        string? category,
        string? product)
    {
        var terms = Tokenize(search);
        return Score(article, terms, category, product, null);
    }

    private static (double Score, string Reason) Score(
        KnowledgeArticle article,
        IReadOnlyList<string> terms,
        string? category,
        string? product,
        string? issueType)
    {
        var score = 0.0;
        var reasons = new List<string>();

        if (!string.IsNullOrWhiteSpace(category) &&
            article.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
        {
            score += 5;
            reasons.Add("category match");
        }

        if (!string.IsNullOrWhiteSpace(issueType) &&
            article.IssueType.Equals(issueType, StringComparison.OrdinalIgnoreCase))
        {
            score += 4;
            reasons.Add("issue type match");
        }

        if (!string.IsNullOrWhiteSpace(product))
        {
            if (article.ApplicableProduct.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                score += 1;
                reasons.Add("applies to all products");
            }
            else if (ContainsTerm(article.ApplicableProduct, product) ||
                     ContainsTerm(product, article.ApplicableProduct))
            {
                score += 3;
                reasons.Add("product match");
            }
        }

        foreach (var term in terms)
        {
            if (article.Tags.Any(t => t.Equals(term, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2.5;
                AddReason(reasons, $"tag '{term}'");
            }

            if (article.Keywords.Any(k => k.Equals(term, StringComparison.OrdinalIgnoreCase)))
            {
                score += 2;
                AddReason(reasons, $"keyword '{term}'");
            }

            if (ContainsTerm(article.Title, term))
            {
                score += 1.5;
                AddReason(reasons, $"title '{term}'");
            }

            if (ContainsTerm(article.CustomerExplanation, term) ||
                ContainsTerm(article.Content, term) ||
                ContainsTerm(article.AgentGuidance, term))
            {
                score += 0.5;
                AddReason(reasons, "article text");
            }
        }

        if (article.Priority.Equals("High", StringComparison.OrdinalIgnoreCase) && score > 0)
        {
            score += 0.5;
            reasons.Add("high-priority article");
        }

        return (Math.Round(score, 2), reasons.Count == 0 ? "no specific match" : string.Join(", ", reasons.Take(4)));
    }

    private static bool ContainsTerm(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    private static void AddReason(List<string> reasons, string reason)
    {
        if (!reasons.Contains(reason))
        {
            reasons.Add(reason);
        }
    }
}
