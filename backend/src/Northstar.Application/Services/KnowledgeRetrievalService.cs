using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Ai;

namespace Northstar.Application.Services;

/// <inheritdoc />
public sealed class KnowledgeRetrievalService(IApplicationDbContext db) : IKnowledgeRetrievalService
{
    public async Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        KnowledgeRetrievalQuery query,
        int maxResults = 4,
        CancellationToken ct = default)
    {
        var articles = await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(ct);

        return articles
            .Select(article =>
            {
                var (score, reason) = KnowledgeScoring.ScoreTicket(
                    article,
                    query.TicketCategory,
                    query.Subject,
                    query.Description,
                    query.ConversationBody,
                    query.ApplicableProduct);
                return (Article: article, Score: score, Reason: reason);
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Article.Title)
            .Take(Math.Clamp(maxResults, 1, 8))
            .Select(x => new KnowledgeHit(
                x.Article.Id,
                x.Article.ArticleCode,
                x.Article.Title,
                x.Article.Category,
                x.Article.ApplicableProduct,
                x.Article.IssueType,
                x.Article.Priority,
                x.Article.ResolutionSteps,
                x.Article.CustomerExplanation,
                x.Article.AgentGuidance,
                x.Article.EscalationCriteria,
                x.Score,
                x.Reason))
            .ToList();
    }
}

/// <summary>Used by unit tests and as a safe no-op when no store is configured.</summary>
internal sealed class EmptyKnowledgeRetrievalService : IKnowledgeRetrievalService
{
    public Task<IReadOnlyList<KnowledgeHit>> RetrieveAsync(
        KnowledgeRetrievalQuery query,
        int maxResults = 4,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<KnowledgeHit>>(Array.Empty<KnowledgeHit>());
}
