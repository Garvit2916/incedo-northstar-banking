using System.Net;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// Exercises role-based authorization on the controller surface. Wrong-role
/// callers must be rejected with 403 before any resource-level logic runs.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class AuthorizationMatrixTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    public static TheoryData<string, string, string> ForbiddenCombinations() => new()
    {
        // account, path, description
        { CustomerEmail, "/api/reviews", "customer cannot browse the review queue" },
        { CustomerEmail, "/api/admin/users", "customer cannot list users" },
        { CustomerEmail, "/api/tickets", "customer cannot access the support workspace" },
        { ReviewerEmail, "/api/admin/users", "reviewer cannot manage users" },
        { AgentEmail, "/api/reviews", "agent cannot browse the review queue" },
        { AgentEmail, "/api/admin/users", "agent cannot manage users" }
    };

    [Theory]
    [MemberData(nameof(ForbiddenCombinations))]
    public async Task Role_gated_endpoints_reject_wrong_roles(string email, string path, string because)
    {
        using var client = await CreateAuthenticatedClientAsync(email);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(because));
    }

    [Fact]
    public async Task Anonymous_requests_to_protected_endpoints_return_unauthorized()
    {
        var response = await Client.GetAsync("/api/products");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
