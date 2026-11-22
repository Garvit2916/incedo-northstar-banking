using System.Net;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AccountOverviewEndpointTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Customer_can_read_a_masked_demo_account_overview()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);

        var response = await customer.GetAsync("/api/dashboard/account-overview");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var root = body.RootElement;
        var masked = root.GetProperty("maskedAccountNumber").GetString();

        Assert.False(string.IsNullOrWhiteSpace(masked));
        Assert.Contains("••••", masked);
        Assert.True(root.GetProperty("availableBalance").GetDecimal() > 0);
        Assert.True(root.GetProperty("transactions").GetArrayLength() > 0);
        Assert.True(root.GetProperty("activeProducts").GetArrayLength() > 0);
        Assert.Contains("demo", root.GetProperty("demoNotice").GetString()!.ToLowerInvariant());

        foreach (var transaction in root.GetProperty("transactions").EnumerateArray())
        {
            Assert.Contains("sample", transaction.GetProperty("narration").GetString()!.ToLowerInvariant());
        }
    }

    [Fact]
    public async Task Operations_users_cannot_read_the_customer_account_overview()
    {
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);

        var response = await agent.GetAsync("/api/dashboard/account-overview");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
