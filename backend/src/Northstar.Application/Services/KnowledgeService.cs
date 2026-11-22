using Microsoft.EntityFrameworkCore;
using Northstar.Application.Abstractions;
using Northstar.Application.Common;
using Northstar.Application.Contracts;
using Northstar.Domain.Entities;

namespace Northstar.Application.Services;

/// <summary>Search and read use cases for the support knowledge base.</summary>
public sealed class KnowledgeService(IApplicationDbContext db)
{
    public async Task<PagedResult<KnowledgeArticleResponse>> SearchAsync(
        KnowledgeQuery query,
        CancellationToken ct)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var articles = await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => a.IsActive)
            .ToListAsync(ct);

        var filtered = articles.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            filtered = filtered.Where(a =>
                a.Category.Equals(query.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Product))
        {
            filtered = filtered.Where(a =>
                a.ApplicableProduct.Equals("All", StringComparison.OrdinalIgnoreCase) ||
                a.ApplicableProduct.Contains(query.Product, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var terms = KnowledgeScoring.Tokenize(query.Search);
            filtered = filtered
                .Select(a => new
                {
                    Article = a,
                    Score = KnowledgeScoring.ScoreSearch(a, query.Search, query.Category, query.Product).Score
                })
                .Where(x => x.Score > 0 || terms.Count == 0)
                .OrderByDescending(x => x.Score)
                .ThenBy(x => x.Article.Title)
                .Select(x => x.Article);
        }
        else
        {
            filtered = filtered.OrderBy(a => a.Category).ThenBy(a => a.Title);
        }

        var all = filtered.ToList();
        var items = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(Map)
            .ToList();

        return new PagedResult<KnowledgeArticleResponse>(items, page, pageSize, all.Count);
    }

    public async Task<IReadOnlyList<string>> CategoriesAsync(CancellationToken ct) =>
        (await db.KnowledgeArticles
            .AsNoTracking()
            .Where(a => a.IsActive)
            .Select(a => a.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct));

    public async Task<KnowledgeArticleResponse> GetAsync(Guid id, CancellationToken ct)
    {
        var article = await db.KnowledgeArticles
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.IsActive, ct)
            ?? throw new NotFoundException("Knowledge article was not found.");

        return Map(article);
    }

    private static KnowledgeArticleResponse Map(KnowledgeArticle a) =>
        new(
            a.Id,
            a.ArticleCode,
            a.Title,
            a.Category,
            a.Tags,
            a.Keywords,
            a.Content,
            a.ApplicableProduct,
            a.IssueType,
            a.Priority,
            a.ResolutionSteps,
            a.CustomerExplanation,
            a.AgentGuidance,
            a.EscalationCriteria,
            a.IsActive,
            a.CreatedAtUtc,
            a.UpdatedAtUtc);
}
