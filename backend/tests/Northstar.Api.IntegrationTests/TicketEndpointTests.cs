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
