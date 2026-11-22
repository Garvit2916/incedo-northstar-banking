using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Northstar.Application.Ai;
using Northstar.Application.Contracts;
using Northstar.Domain;
using Northstar.Domain.Entities;

namespace Northstar.Application.Tests;

/// <summary>
/// Covers AI orchestration guarantees that matter to the assessment: persisted
/// sentiment, deterministic next-best actions with expected outcomes, and
/// provenance that never claims live inference when a fallback produced output.
/// </summary>
public sealed class AiAssistanceServiceTests : IDisposable
{
    private readonly ServiceFixture _fx = new();

    public void Dispose() => _fx.Dispose();

    [Fact]
    public async Task Sentiment_is_persisted_on_the_ticket_and_exposed_in_the_overview()
    {
        var ticket = await CreateTicketAsync();

        var result = await _fx.Ai.AnalyzeSentimentAsync(ticket.Id, default);

        Assert.Equal(SentimentLabel.Negative, result.Label);
        Assert.Equal(new[] { "frustrated" }, result.FrustrationIndicators);
        Assert.Equal(new[] { "still waiting" }, result.UrgencyIndicators);

        var stored = await _fx.Db.Tickets.AsNoTracking().SingleAsync(t => t.Id == ticket.Id);
        Assert.Equal(SentimentLabel.Negative, stored.LatestSentiment);
        Assert.Equal(_fx.Clock.UtcNow, stored.SentimentAnalyzedAtUtc);

        var overview = await _fx.Tickets.GetOverviewAsync(default);
        Assert.Equal(1, overview.NegativeSentiment);
        Assert.Equal(1, overview.AnalyzedSentiment);
        Assert.Equal(0, overview.UrgentEscalationSentiment);
        Assert.Contains("ai.sentimentanalysis", _fx.Audit.Actions);
    }

    [Fact]
    public async Task Next_best_actions_include_an_expected_outcome_and_action_type()
    {
        var ticket = await CreateTicketAsync(
            category: TicketCategory.AccountAccess,
            subject: "Locked out of my account",
            description: "I am locked out and cannot access my account. This is urgent.");

        var response = await _fx.Ai.NextBestActionsAsync(ticket.Id, default);

        Assert.NotEmpty(response.Actions);
        Assert.All(response.Actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.ExpectedOutcome));
            Assert.Contains(action.ActionType, new[]
            {
                NextBestActionTypes.Reply,
                NextBestActionTypes.RequestDocuments,
                NextBestActionTypes.VerifyIdentity,
                NextBestActionTypes.ReviewApplication,
                NextBestActionTypes.Escalate,
                NextBestActionTypes.Resolve,
                NextBestActionTypes.FollowUp,
                NextBestActionTypes.Clarify
            });
        });

        Assert.Contains(response.Actions, action =>
            action.ActionType == NextBestActionTypes.VerifyIdentity);
        Assert.False(response.Provenance.IsLiveInference);
        Assert.Equal("deterministic-rules", response.Provenance.Provider);
        Assert.True(response.Provenance.RequiresHumanReview);
        Assert.Contains("ai.next-best-actions", _fx.Audit.Actions);
    }

    [Fact]
    public async Task Next_best_actions_only_use_application_context_for_authorized_roles()
    {
        var customer = _fx.SeedUser();
        var profile = _fx.SeedProfile(customer.Id);
        var product = _fx.SeedProduct();
        var application = Northstar.Domain.Entities.Application.CreateDraft(
            profile.Id, product.Id, "NS-APP-PRIVATE", null);
        _fx.Db.Applications.Add(application);
        await _fx.Db.SaveChangesAsync(default);

        var ticket = await CreateTicketAsync(relatedApplicationId: application.Id);

        var unprivileged = await _fx.Ai.NextBestActionsAsync(ticket.Id, default);
        Assert.DoesNotContain("NS-APP-PRIVATE", JsonSerializer.Serialize(unprivileged));

        _fx.Caller.Roles.Add(Roles.Reviewer);
        var privileged = await _fx.Ai.NextBestActionsAsync(ticket.Id, default);
        Assert.Contains("NS-APP-PRIVATE", JsonSerializer.Serialize(privileged));
    }

    [Fact]
    public async Task Summary_provenance_labels_the_provider_and_requires_human_review()
    {
        var ticket = await CreateTicketAsync();

        var summary = await _fx.Ai.SummarizeAsync(ticket.Id, default);

        Assert.Equal("Customer is waiting for an update.", summary.MainIssue);
        Assert.Equal("test-provider", summary.Provenance.Provider);
        Assert.Equal("test-model", summary.Provenance.Model);
        Assert.False(summary.Provenance.IsLiveInference);
        Assert.True(summary.Provenance.RequiresHumanReview);
        Assert.Contains("assistive only", summary.Provenance.Disclaimer);
    }

    private async Task<TicketSummaryResponse> CreateTicketAsync(
        TicketCategory category = TicketCategory.StatusEnquiry,
        string subject = "Where is my application?",
        string description = "I submitted last week, I am frustrated and still waiting for an update.",
        Guid? relatedApplicationId = null)
    {
        var customer = _fx.SeedUser();
        _fx.Caller.UserId = customer.Id;

        return await _fx.Tickets.CreateAsync(
            new CreateTicketRequest(
                subject,
                description,
                category,
                TicketPriority.Normal,
                customer.Id,
                relatedApplicationId),
            default);
    }
}
