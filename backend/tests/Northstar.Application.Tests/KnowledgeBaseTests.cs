using Northstar.Application.Ai;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers deterministic knowledge retrieval, assistive issue classification and
/// the citation/workflow metadata attached to grounded next-best actions.
/// </summary>
public sealed class KnowledgeBaseTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Search_ranks_articles_by_keyword_relevance()
    {
        SeedArticle("KB-100", "Card dispute", "Cards", "dispute;card", "card refund charge", "CardTransactionDispute");
        var upi = SeedArticle("KB-101", "Failed UPI transaction", "Payments", "upi;refund", "upi;failed;refund;utr", "FailedUpiTransaction");

        var result = await _fx.KnowledgeBase.SearchAsync(
            new KnowledgeQuery("upi refund", null, null, 1, 10), default);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal("KB-101", result.Items[0].ArticleCode);
        Assert.Contains("upi", result.Items[0].Keywords);
        Assert.NotEmpty(result.Items[0].ResolutionSteps);
    }

    [Fact]
    public async Task Retrieval_scores_the_subject_description_and_conversation()
    {
        var article = SeedArticle("KB-102", "Failed UPI transaction", "Payments", "upi;refund",
            "upi refund utr failed", "FailedUpiTransaction");

        var hits = await _fx.Retrieval.RetrieveAsync(new KnowledgeRetrievalQuery(
            "FailedUpiTransaction",
            "Payment failed",
            "My UPI payment failed and the money was deducted.",
            new[] { "Please check the UTR and refund." },
            "All"), 4, default);

        var hit = Assert.Single(hits);
        Assert.Equal(article.ArticleCode, hit.ArticleCode);
        Assert.True(hit.Score > 0);
        Assert.Contains("category match", hit.MatchReason);
        Assert.Contains("issue type", hit.MatchReason);
        Assert.True(hit.Score > 5, "subject, description and conversation terms contribute to the score");
    }

    [Fact]
    public void Issue_classifier_flags_fraud_from_untrusted_ticket_text()
    {
        var context = new AiTicketContext(
            "NS-TKT-TEST",
            "Charge I did not make",
            "CardTransactionDispute",
            "High",
            "Open",
            new[] { new AiConversationMessage("customer", "This looks like fraud; somebody else used my card.") },
            null,
            "I see an unauthorized transaction that I did not authorise.");

        var result = IssueClassifier.Classify(context);

        Assert.Equal(nameof(TicketCategory.UnauthorizedTransaction), result.SuggestedCategory);
        Assert.True(result.Confidence >= 0.4);
        Assert.NotEmpty(result.Signals);
    }

    [Fact]
    public void Grounded_action_cites_the_article_and_suggests_a_safe_workflow()
    {
        var hit = MakeHit();

        var actions = NextBestActionEngine.Recommend(new NextBestActionInput(
            TicketCategory.UnauthorizedTransaction,
            TicketStatus.Open,
            TicketPriority.High,
            1,
            true,
            null,
            SentimentLabel.UrgentEscalation,
            "Charge I did not make",
            "Unauthorized transaction that I did not authorise.",
            new[] { "I think this is fraud." },
            new[] { hit }));

        Assert.Contains(actions, action =>
            action.Sources.Any(source => source.ArticleCode == hit.ArticleCode) &&
            action.SuggestedWorkflow == "fraud-review-confirmation" &&
            action.Confidence is > 0 and <= 0.95);
        Assert.All(actions, action => Assert.True(action.RequiresHumanApproval));
    }

    [Fact]
    public async Task Suggested_response_exposes_knowledge_sources_without_claiming_live_inference()
    {
        var customer = _fx.SeedUser();
        _fx.Caller.UserId = customer.Id;
        var ticket = await _fx.Tickets.CreateAsync(
            new CreateTicketRequest(
                "Unrecognised card charge",
                "I did not authorise a card transaction and suspect fraud.",
                TicketCategory.UnauthorizedTransaction,
                TicketPriority.High,
                customer.Id,
                null),
            default);
        _fx.Knowledge.Hits = new[] { MakeHit() };

        var response = await _fx.Ai.SuggestResponseAsync(ticket.Id, default);

        Assert.Contains(response.Sources, source => source.ArticleCode == "KB-900");
        Assert.False(response.Provenance.IsLiveInference);
        Assert.True(response.Provenance.RequiresHumanReview);
    }

    [Fact]
    public async Task Next_best_actions_include_classification_and_article_references()
    {
        var customer = _fx.SeedUser();
        _fx.Caller.UserId = customer.Id;
        var ticket = await _fx.Tickets.CreateAsync(
            new CreateTicketRequest(
                "UPI refund not received",
                "My UPI payment failed, money was deducted and I need a refund.",
                TicketCategory.Other,
                TicketPriority.High,
                customer.Id,
                null),
            default);
        var hit = MakeHit("KB-901", "Failed UPI transaction", "FailedUpiTransaction");
        _fx.Knowledge.Hits = new[] { hit };

        var response = await _fx.Ai.NextBestActionsAsync(ticket.Id, default);

        Assert.NotNull(response.Classification);
        Assert.Equal(nameof(TicketCategory.FailedUpiTransaction), response.Classification.SuggestedCategory);
        Assert.Contains(response.Actions, action =>
            action.Sources.Any(source => source.ArticleCode == "KB-901") &&
            !string.IsNullOrWhiteSpace(action.SuggestedWorkflow));
        Assert.All(response.Actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.Title));
            Assert.False(string.IsNullOrWhiteSpace(action.Explanation));
            Assert.True(action.RequiresHumanApproval);
        });
    }

    private KnowledgeArticle SeedArticle(
        string code,
        string title,
        string category,
        string tags,
        string keywords,
        string issueType)
    {
        var article = new KnowledgeArticle
        {
            ArticleCode = code,
            Title = title,
            Category = category,
            TagsCsv = tags,
            KeywordsCsv = keywords,
            Content = title + " " + keywords,
            ApplicableProduct = "All",
            IssueType = issueType,
            Priority = "High",
            CustomerExplanation = "We can review this for you.",
            AgentGuidance = "Verify identity before discussing details.",
            EscalationCriteria = "Escalate if suspected fraud.",
            ResolutionStepsJson = "[\"Collect the reference\",\"Record the review\"]",
            CreatedAtUtc = _fx.Clock.UtcNow,
            UpdatedAtUtc = _fx.Clock.UtcNow
        };

        _fx.Db.KnowledgeArticles.Add(article);
        _fx.Db.SaveChanges();
        return article;
    }

    private static KnowledgeHit MakeHit(
        string code = "KB-900",
        string title = "Unauthorized transaction and suspected fraud",
        string issueType = "UnauthorizedTransaction") =>
        new(
            Guid.NewGuid(),
            code,
            title,
            "Security",
            "Credit Card",
            issueType,
            "High",
            new[] { "Verify the customer", "Escalate to the fraud team" },
            "We can help protect your card.",
            "Never ask for OTP or PIN.",
            "Escalate suspected fraud immediately.",
            9.5,
            "keyword 'fraud', issue type match");
}
