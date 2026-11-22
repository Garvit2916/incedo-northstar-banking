using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class KnowledgeEndpointTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Agent_can_search_the_seeded_knowledge_base()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);

        var response = await agent.GetAsync("/api/knowledge?search=UPI&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.NotEmpty(items);
        Assert.Contains(items, item =>
            item.GetProperty("title").GetString()!.Contains("UPI", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Agent_can_read_categories_and_article_detail()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);

        var categoriesResponse = await agent.GetAsync("/api/knowledge/categories");
        categoriesResponse.EnsureSuccessStatusCode();
        using var categories = await ReadJsonAsync(categoriesResponse);
        Assert.NotEmpty(categories.RootElement.EnumerateArray());

        var listResponse = await agent.GetAsync("/api/knowledge?pageSize=5");
        listResponse.EnsureSuccessStatusCode();
        using var list = await ReadJsonAsync(listResponse);
        var id = list.RootElement.GetProperty("items")[0].GetProperty("id").GetGuid();

        var detailResponse = await agent.GetAsync($"/api/knowledge/{id}");
        detailResponse.EnsureSuccessStatusCode();
        using var detail = await ReadJsonAsync(detailResponse);
        Assert.False(string.IsNullOrWhiteSpace(detail.RootElement.GetProperty("content").GetString()));
        Assert.True(detail.RootElement.GetProperty("resolutionSteps").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Customer_cannot_access_the_agent_knowledge_base()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);

        var response = await customer.GetAsync("/api/knowledge");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Next_best_actions_cite_knowledge_for_a_card_dispute()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var ticketId = await FindTicketAsync(agent, "NS-TKT-1006");
        Assert.NotEqual(Guid.Empty, ticketId);

        var response = await agent.PostAsync($"/api/tickets/{ticketId}/ai/next-best-actions", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.TryGetProperty("classification", out var classification));
        Assert.False(string.IsNullOrWhiteSpace(classification.GetProperty("suggestedCategory").GetString()));

        var actions = body.RootElement.GetProperty("actions").EnumerateArray().ToList();
        Assert.NotEmpty(actions);
        Assert.Contains(actions, action =>
        {
            if (!action.TryGetProperty("sources", out var sources))
            {
                return false;
            }

            return sources.EnumerateArray().Any(source =>
                source.GetProperty("articleCode").GetString()!.StartsWith("KB-"));
        });
        Assert.All(actions, action =>
        {
            Assert.False(string.IsNullOrWhiteSpace(action.GetProperty("title").GetString()));
            Assert.True(action.GetProperty("requiresHumanApproval").GetBoolean());
        });
    }

    private static async Task<Guid> FindTicketAsync(HttpClient client, string reference)
    {
        var response = await client.GetAsync("/api/tickets?pageSize=100");
        response.EnsureSuccessStatusCode();
        using var body = await ReadJsonAsync(response);

        foreach (var item in body.RootElement.GetProperty("items").EnumerateArray())
        {
            if (item.GetProperty("reference").GetString() == reference)
            {
                return item.GetProperty("id").GetGuid();
            }
        }

        return Guid.Empty;
    }
}
