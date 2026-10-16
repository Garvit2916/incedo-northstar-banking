using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// End-to-end coverage of the onboarding lifecycle over real HTTP: draft,
/// edit, submit, review, approve/reject - plus cross-tenant isolation and the
/// conflict rules enforced by the application state machine.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApplicationFlowTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    private static readonly Dictionary<string, string> CompleteAnswers = new()
    {
        ["employmentStatus"] = "Employed",
        ["purpose"] = "Everyday banking"
    };

    [Fact]
    public async Task Customer_can_complete_the_full_onboarding_lifecycle()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        var productId = await GetSavingsProductIdAsync(customer);

        using var created = await CreateDraftAsync(customer, productId);
        var id = created.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.RootElement.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(created.RootElement.GetProperty("reference").GetString()));

        var updateResponse = await customer.PutAsJsonAsync(
            $"/api/applications/{id}", new { answers = CompleteAnswers });
        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);

        var submitResponse = await customer.PostAsync($"/api/applications/{id}/submit", null);
        Assert.Equal(HttpStatusCode.OK, submitResponse.StatusCode);
        using var submitted = await ReadJsonAsync(submitResponse);
        Assert.Equal("Submitted", submitted.RootElement.GetProperty("status").GetString());

        var mineResponse = await customer.GetAsync("/api/applications");
        mineResponse.EnsureSuccessStatusCode();
        using var mine = await ReadJsonAsync(mineResponse);
        Assert.Contains(mine.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);

        var historyResponse = await customer.GetAsync($"/api/applications/{id}/history");
        historyResponse.EnsureSuccessStatusCode();
        using var history = await ReadJsonAsync(historyResponse);
        Assert.Contains(history.RootElement.EnumerateArray(),
            entry => entry.GetProperty("toStatus").GetString() == "Submitted");

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);

        var queueResponse = await reviewer.GetAsync("/api/reviews");
        queueResponse.EnsureSuccessStatusCode();
        using var queue = await ReadJsonAsync(queueResponse);
        Assert.Contains(queue.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == id);

        var startResponse = await reviewer.PostAsync($"/api/reviews/{id}/start", null);
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        using var started = await ReadJsonAsync(startResponse);
        Assert.Equal("UnderReview", started.RootElement.GetProperty("status").GetString());

        var approveResponse = await reviewer.PostAsJsonAsync(
            $"/api/reviews/{id}/approve", new { reason = "Meets all onboarding criteria." });
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);
        using var approved = await ReadJsonAsync(approveResponse);
        Assert.Equal("Approved", approved.RootElement.GetProperty("status").GetString());
        Assert.Equal("Meets all onboarding criteria.",
            approved.RootElement.GetProperty("decisionReason").GetString());
    }

    [Fact]
    public async Task Approved_application_can_no_longer_be_edited()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        var id = await SubmitNewApplicationAsync(customer);

        using var reviewer = await CreateAuthenticatedClientAsync(ReviewerEmail);
        (await reviewer.PostAsync($"/api/reviews/{id}/start", null)).EnsureSuccessStatusCode();
        (await reviewer.PostAsJsonAsync($"/api/reviews/{id}/approve", new { reason = "Approved for test." }))
            .EnsureSuccessStatusCode();

        var editResponse = await customer.PutAsJsonAsync(
            $"/api/applications/{id}", new { answers = CompleteAnswers });

        Assert.Equal(HttpStatusCode.Conflict, editResponse.StatusCode);
    }

    [Fact]
    public async Task Withdrawn_application_cannot_be_resubmitted()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        var productId = await GetSavingsProductIdAsync(customer);
        using var created = await CreateDraftAsync(customer, productId);
        var id = created.RootElement.GetProperty("id").GetGuid();

        var withdrawResponse = await customer.PostAsync($"/api/applications/{id}/withdraw", null);
        Assert.Equal(HttpStatusCode.OK, withdrawResponse.StatusCode);
        using var withdrawn = await ReadJsonAsync(withdrawResponse);
        Assert.Equal("Withdrawn", withdrawn.RootElement.GetProperty("status").GetString());

        var resubmitResponse = await customer.PostAsync($"/api/applications/{id}/submit", null);
        Assert.Equal(HttpStatusCode.Conflict, resubmitResponse.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_read_another_customers_application()
    {
        using var owner = await CreateAuthenticatedClientAsync(CustomerEmail);
        var id = await SubmitNewApplicationAsync(owner);

        using var otherCustomer = await RegisterNewCustomerAsync();

        var response = await otherCustomer.GetAsync($"/api/applications/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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

    private async Task<JsonDocument> CreateDraftAsync(HttpClient customer, Guid productId)
    {
        var response = await customer.PostAsJsonAsync("/api/applications", new
        {
            productId,
            answers = new Dictionary<string, string>()
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    private async Task<Guid> SubmitNewApplicationAsync(HttpClient customer)
    {
        var productId = await GetSavingsProductIdAsync(customer);
        using var created = await CreateDraftAsync(customer, productId);
        var id = created.RootElement.GetProperty("id").GetGuid();

        (await customer.PutAsJsonAsync($"/api/applications/{id}", new { answers = CompleteAnswers }))
            .EnsureSuccessStatusCode();
        (await customer.PostAsync($"/api/applications/{id}/submit", null)).EnsureSuccessStatusCode();

        return id;
    }

    private async Task<HttpClient> RegisterNewCustomerAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"other-{Guid.NewGuid():N}@northstar.example",
            password = "Northstar#New01",
            firstName = "Other",
            lastName = "Customer",
            phoneNumber = "+1-555-0188"
        });
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        var token = body.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
