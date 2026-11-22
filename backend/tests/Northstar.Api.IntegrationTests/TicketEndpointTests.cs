using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// Covers the AI-assisted support workspace: listing, appending messages,
/// assignment and the deterministic AI assist endpoints (including provenance).
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class TicketEndpointTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Agent_can_list_the_seeded_ticket_queue()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);

        var response = await agent.GetAsync("/api/tickets");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.GetProperty("items").GetArrayLength() >= 5,
            "the demonstration dataset seeds five tickets");
    }

    [Fact]
    public async Task Agent_can_append_a_message_to_a_ticket()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var ticketId = await GetFirstTicketIdAsync(agent);

        var before = await GetMessageCountAsync(agent, ticketId);

        var response = await agent.PostAsJsonAsync($"/api/tickets/{ticketId}/messages", new
        {
            body = "Thanks for reaching out - I am looking into this now.",
            isInternalNote = false
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(before + 1, body.RootElement.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task Agent_can_assign_a_ticket_to_themselves()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var agentId = await GetCurrentUserIdAsync(agent);
        var ticketId = await GetFirstTicketIdAsync(agent);

        var response = await agent.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/assign", new { agentUserId = agentId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadJsonAsync(response);
        Assert.Equal(agentId, body.RootElement.GetProperty("assignedAgentId").GetGuid());
    }

    [Fact]
    public async Task Summarize_returns_a_result_with_provenance()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var ticketId = await GetFirstTicketIdAsync(agent);

        var response = await agent.PostAsync($"/api/tickets/{ticketId}/ai/summarize", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("mainIssue").GetString()));

        var provenance = body.RootElement.GetProperty("provenance");
        Assert.False(provenance.GetProperty("isLiveInference").GetBoolean());
        Assert.True(provenance.GetProperty("requiresHumanReview").GetBoolean());
    }

    [Fact]
    public async Task Agent_can_read_the_support_overview()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);

        var response = await agent.GetAsync("/api/tickets/overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.GetProperty("openTickets").GetInt32() >= 0);
        Assert.True(body.RootElement.GetProperty("negativeSentiment").GetInt32() >= 0);
        Assert.True(body.RootElement.GetProperty("urgentEscalationSentiment").GetInt32() >= 0);
        Assert.True(body.RootElement.GetProperty("analyzedSentiment").GetInt32() >= 0);
    }

    [Fact]
    public async Task Sentiment_analysis_is_persisted_and_returned_with_the_ticket()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var ticketId = await GetFirstTicketIdAsync(agent);

        var response = await agent.PostAsync($"/api/tickets/{ticketId}/ai/analyze-sentiment", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var label = body.RootElement.GetProperty("label").GetString();
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.True(body.RootElement.TryGetProperty("frustrationIndicators", out _));
        Assert.True(body.RootElement.TryGetProperty("urgencyIndicators", out _));

        var detailResponse = await agent.GetAsync($"/api/tickets/{ticketId}");
        detailResponse.EnsureSuccessStatusCode();

        using var detail = await ReadJsonAsync(detailResponse);
        Assert.Equal(label, detail.RootElement.GetProperty("latestSentiment").GetString());
        Assert.Equal(JsonValueKind.String,
            detail.RootElement.GetProperty("sentimentAnalyzedAtUtc").ValueKind);
    }

    [Fact]
    public async Task Next_best_actions_return_expected_outcomes_and_action_types()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var ticketId = await GetFirstTicketIdAsync(agent);

        var response = await agent.PostAsync($"/api/tickets/{ticketId}/ai/next-best-actions", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var actions = body.RootElement.GetProperty("actions").EnumerateArray().ToList();
        Assert.NotEmpty(actions);
        Assert.All(actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.GetProperty("expectedOutcome").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(action.GetProperty("actionType").GetString()));
        });
    }

    [Fact]
    public async Task Next_best_actions_do_not_leak_related_application_to_support_agents()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        var productId = await GetSavingsProductIdAsync(customer);
        var createApplication = await customer.PostAsJsonAsync("/api/applications", new
        {
            productId,
            answers = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, createApplication.StatusCode);

        using var applicationBody = await ReadJsonAsync(createApplication);
        var applicationId = applicationBody.RootElement.GetProperty("id").GetGuid();
        var applicationReference = applicationBody.RootElement.GetProperty("reference").GetString()!;

        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var createTicket = await agent.PostAsJsonAsync("/api/tickets", new
        {
            subject = "Application status question",
            description = "Please tell me the status of my application.",
            category = "StatusEnquiry",
            priority = "Normal",
            relatedApplicationId = applicationId
        });
        Assert.Equal(HttpStatusCode.Created, createTicket.StatusCode);

        using var ticketBody = await ReadJsonAsync(createTicket);
        var ticketId = ticketBody.RootElement.GetProperty("id").GetGuid();

        var agentAi = await agent.PostAsync($"/api/tickets/{ticketId}/ai/next-best-actions", null);
        Assert.Equal(HttpStatusCode.OK, agentAi.StatusCode);
        using var agentAiBody = await ReadJsonAsync(agentAi);
        Assert.DoesNotContain(applicationReference, agentAiBody.RootElement.GetRawText());

        using var admin = await CreateAuthenticatedClientAsync(AdminEmail);
        var adminAi = await admin.PostAsync($"/api/tickets/{ticketId}/ai/next-best-actions", null);
        Assert.Equal(HttpStatusCode.OK, adminAi.StatusCode);
        using var adminAiBody = await ReadJsonAsync(adminAi);
        Assert.Contains(applicationReference, adminAiBody.RootElement.GetRawText());
    }

    private static async Task<Guid> GetSavingsProductIdAsync(HttpClient customer)
    {
        var response = await customer.GetAsync("/api/products");
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        var product = body.RootElement.EnumerateArray()
            .First(p => p.GetProperty("code").GetString() == "SAV-001");
        return product.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> GetFirstTicketIdAsync(HttpClient agent)
    {
        var response = await agent.GetAsync("/api/tickets");
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("items").EnumerateArray()
            .First().GetProperty("id").GetGuid();
    }

    private static async Task<int> GetMessageCountAsync(HttpClient agent, Guid ticketId)
    {
        var response = await agent.GetAsync($"/api/tickets/{ticketId}");
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("messages").GetArrayLength();
    }

    private static async Task<Guid> GetCurrentUserIdAsync(HttpClient agent)
    {
        var response = await agent.GetAsync("/api/auth/me");
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
