using System.Net;
using System.Net.Http.Json;

namespace Northstar.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class AuthEndpointTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Login_with_valid_credentials_returns_access_token_and_roles()
    {
        var (response, body) = await LoginAsync(CustomerEmail);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = body.RootElement;
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("accessToken").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("refreshToken").GetString()));

        var roles = root.GetProperty("user").GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains("Customer", roles);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_unauthorized()
    {
        var (response, _) = await LoginAsync(CustomerEmail, "definitely-not-the-password");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_without_a_token_returns_unauthorized()
    {
        var response = await Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_with_a_valid_token_returns_the_caller()
    {
        using var client = await CreateAuthenticatedClientAsync(CustomerEmail);

        var response = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.Equal(CustomerEmail, body.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Register_creates_a_customer_account()
    {
        var email = $"new-customer-{Guid.NewGuid():N}@northstar.example";
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Northstar#New01",
            firstName = "New",
            lastName = "Customer",
            phoneNumber = "+1-555-0199"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        var roles = body.RootElement.GetProperty("user").GetProperty("roles")
            .EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Contains("Customer", roles);
    }

    [Fact]
    public async Task Refresh_issues_a_new_token_pair()
    {
        var (_, loginBody) = await LoginAsync(CustomerEmail);
        var refreshToken = loginBody.RootElement.GetProperty("refreshToken").GetString();

        var response = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Register_rejects_a_weak_password_with_field_errors()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"weak-{Guid.NewGuid():N}@northstar.example",
            password = "password",
            confirmPassword = "password",
            firstName = "Weak",
            lastName = "Password"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("Password", out var passwordErrors));
        Assert.NotEmpty(passwordErrors.EnumerateArray());
    }

    [Fact]
    public async Task Register_rejects_a_mismatched_confirmation()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"mismatch-{Guid.NewGuid():N}@northstar.example",
            password = "Northstar#New01",
            confirmPassword = "Northstar#Different01",
            firstName = "Mismatch",
            lastName = "Password"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var body = await ReadJsonAsync(response);
        Assert.True(body.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("ConfirmPassword", out _));
    }

    [Fact]
    public async Task Register_accepts_a_valid_password_with_matching_confirmation()
    {
        var email = $"valid-{Guid.NewGuid():N}@northstar.example";
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            email,
            password = "Northstar#Valid01",
            confirmPassword = "Northstar#Valid01",
            firstName = "Valid",
            lastName = "Customer"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_rejects_a_duplicate_email()
    {
        var email = $"duplicate-{Guid.NewGuid():N}@northstar.example";
        var payload = new
        {
            email,
            password = "Northstar#Dup01",
            confirmPassword = "Northstar#Dup01",
            firstName = "Dup",
            lastName = "Customer"
        };

        var first = await Client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await Client.PostAsJsonAsync("/api/auth/register", payload);
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);

        using var body = await ReadJsonAsync(second);
        var detail = body.RootElement.GetProperty("detail").GetString();
        Assert.Contains("already exists", detail);
    }
}
