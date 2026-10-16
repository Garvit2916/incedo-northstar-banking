using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// Shared behaviour for HTTP-level tests: seeds the database once per run and
/// provides helpers for authenticated clients and JSON assertions. Concrete
/// test classes must apply <c>[Collection(ApiCollection.Name)]</c> so they share
/// the single seeded <see cref="NorthstarApiFactory"/>.
/// </summary>
public abstract class ApiTestBase : IAsyncLifetime
{
    protected const string DefaultPassword = "Northstar#Dev01";
    protected const string CustomerEmail = "customer@northstar.example";
    protected const string ReviewerEmail = "reviewer@northstar.example";
    protected const string AgentEmail = "agent@northstar.example";
    protected const string AdminEmail = "admin@northstar.example";

    protected NorthstarApiFactory Factory { get; }
    protected HttpClient Client { get; private set; } = default!;

    protected ApiTestBase(NorthstarApiFactory factory)
    {
        Factory = factory;
    }

    public async Task InitializeAsync()
    {
        await Factory.EnsureSeededAsync();
        Client = Factory.CreateClient();
    }

    public Task DisposeAsync()
    {
        Client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Creates a client authenticated as the supplied seeded account.</summary>
    protected async Task<HttpClient> CreateAuthenticatedClientAsync(
        string email,
        string password = DefaultPassword)
    {
        var client = Factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();

        var document = await ReadJsonAsync(response);
        var token = document.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    protected static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var payload = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(payload);
    }

    /// <summary>Logs in anonymously and returns the full authentication response document.</summary>
    protected async Task<(HttpResponseMessage Response, JsonDocument Body)> LoginAsync(
        string email,
        string password = DefaultPassword)
    {
        var response = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (response, body);
    }
}
