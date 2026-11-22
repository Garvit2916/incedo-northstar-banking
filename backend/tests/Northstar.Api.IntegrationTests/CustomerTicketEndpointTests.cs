using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Northstar.Api.IntegrationTests;

/// <summary>
/// HTTP coverage for customer self-service tickets: creation, scoped tracking,
/// role gates, related-application isolation, and internal-note privacy.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class CustomerTicketEndpointTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Customer_can_create_and_track_a_ticket()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        var marker = $"trace-{Guid.NewGuid():N}";

        var create = await customer.PostAsJsonAsync("/api/customer/tickets", new
        {
            subject = "Card charge I do not recognise",
            description = $"I do not recognise this charge. {marker}",
            category = "CardTransactionDispute",
            priority = "High",
            relatedApplicationId = (Guid?)null
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        using var createdBody = await ReadJsonAsync(create);
        var ticketId = createdBody.RootElement.GetProperty("id").GetGuid();
        var reference = createdBody.RootElement.GetProperty("reference").GetString();
        Assert.False(string.IsNullOrWhiteSpace(reference));
        Assert.NotNull(create.Headers.Location);
        Assert.EndsWith($"/api/customer/tickets/{ticketId}", create.Headers.Location!.OriginalString);
        Assert.False(createdBody.RootElement.TryGetProperty("latestSentiment", out _));

        var list = await customer.GetAsync("/api/customer/tickets?page=1&pageSize=100");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using var listBody = await ReadJsonAsync(list);
        var listed = listBody.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == ticketId);
        Assert.False(listed.TryGetProperty("latestSentiment", out _));

        var detail = await customer.GetAsync($"/api/customer/tickets/{ticketId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailBody = await ReadJsonAsync(detail);
        Assert.Equal("New", detailBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("Customer", detailBody.RootElement.GetProperty("messages")[0].GetProperty("authorType").GetString());
        Assert.Contains(marker, detailBody.RootElement.GetRawText());
        Assert.False(detailBody.RootElement.TryGetProperty("latestSentiment", out _));
        Assert.False(detailBody.RootElement.TryGetProperty("sentimentAnalyzedAtUtc", out _));
    }

    [Theory]
    [InlineData(AgentEmail)]
    [InlineData(AdminEmail)]
    [InlineData(ReviewerEmail)]
    public async Task Customer_ticket_endpoints_reject_operations_roles(string email)
    {
        using var operationsUser = await CreateAuthenticatedClientAsync(email);

        var list = await operationsUser.GetAsync("/api/customer/tickets");
        var create = await operationsUser.PostAsJsonAsync("/api/customer/tickets", new
        {
            subject = "Not allowed",
            description = "Operations roles must use the support workspace.",
            category = "Other",
            priority = "Normal"
        });
        var detail = await operationsUser.GetAsync(
            "/api/customer/tickets/00000000-0000-0000-0000-000000000001");

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, detail.StatusCode);
    }

    [Fact]
    public async Task Customer_cannot_read_another_customers_ticket()
    {
        using var owner = await CreateAuthenticatedClientAsync(CustomerEmail);
        var created = await owner.PostAsJsonAsync("/api/customer/tickets", ValidTicket(Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var ticketId = createdBody.RootElement.GetProperty("id").GetGuid();

        using var other = await RegisterNewCustomerAsync();
        var detail = await other.GetAsync($"/api/customer/tickets/{ticketId}");
        Assert.Equal(HttpStatusCode.NotFound, detail.StatusCode);

        var list = await other.GetAsync("/api/customer/tickets?page=1&pageSize=100");
        using var listBody = await ReadJsonAsync(list);
        Assert.DoesNotContain(listBody.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == ticketId);
    }

    [Fact]
    public async Task Customer_cannot_attach_another_customers_application()
    {
        using var owner = await CreateAuthenticatedClientAsync(CustomerEmail);
        using var other = await RegisterNewCustomerAsync();

        var products = await owner.GetFromJsonAsync<JsonElement[]>("/api/products");
        Assert.NotNull(products);
        var savings = products.First(p => p.GetProperty("code").GetString() == "SAV-001");
        var application = await owner.PostAsJsonAsync("/api/applications", new
        {
            productId = savings.GetProperty("id").GetGuid(),
            answers = new Dictionary<string, string>()
        });
        Assert.Equal(HttpStatusCode.Created, application.StatusCode);
        using var applicationBody = await ReadJsonAsync(application);
        var applicationId = applicationBody.RootElement.GetProperty("id").GetGuid();

        var ownTicket = await owner.PostAsJsonAsync("/api/customer/tickets", new
        {
            subject = "My application question",
            description = "Please explain the status of my application.",
            category = "StatusEnquiry",
            priority = "Normal",
            relatedApplicationId = applicationId
        });
        Assert.Equal(HttpStatusCode.Created, ownTicket.StatusCode);
        using var ownTicketBody = await ReadJsonAsync(ownTicket);
        Assert.Equal(applicationId, ownTicketBody.RootElement.GetProperty("relatedApplicationId").GetGuid());

        var ticket = await other.PostAsJsonAsync("/api/customer/tickets", new
        {
            subject = "Application question",
            description = "Please explain the status of my application.",
            category = "StatusEnquiry",
            priority = "Normal",
            relatedApplicationId = applicationId
        });

        Assert.Equal(HttpStatusCode.NotFound, ticket.StatusCode);
    }

    [Fact]
    public async Task Customer_detail_hides_agent_internal_notes()
    {
        using var customer = await CreateAuthenticatedClientAsync(CustomerEmail);
        using var agent = await CreateAuthenticatedClientAsync(AgentEmail);
        var note = $"internal-only-{Guid.NewGuid():N}";

        var created = await customer.PostAsJsonAsync("/api/customer/tickets", ValidTicket(Guid.NewGuid().ToString("N")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var createdBody = await ReadJsonAsync(created);
        var ticketId = createdBody.RootElement.GetProperty("id").GetGuid();

        var internalNote = await agent.PostAsJsonAsync(
            $"/api/tickets/{ticketId}/messages",
            new { body = note, isInternalNote = true });
        Assert.Equal(HttpStatusCode.OK, internalNote.StatusCode);

        var detail = await customer.GetAsync($"/api/customer/tickets/{ticketId}");
        using var detailBody = await ReadJsonAsync(detail);
        Assert.DoesNotContain(note, detailBody.RootElement.GetRawText());

        var list = await customer.GetAsync("/api/customer/tickets?page=1&pageSize=100");
        using var listBody = await ReadJsonAsync(list);
        var summary = listBody.RootElement.GetProperty("items").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == ticketId);
        Assert.Equal(1, summary.GetProperty("messageCount").GetInt32());
    }

    private static object ValidTicket(string marker) => new
    {
        subject = $"Support request {marker}",
        description = $"Please look into this. {marker}",
        category = "StatusEnquiry",
        priority = "Normal"
    };

    private async Task<HttpClient> RegisterNewCustomerAsync()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"customer-ticket-{Guid.NewGuid():N}@northstar.example",
            password = "Northstar#New01",
            firstName = "Ticket",
            lastName = "Customer",
            phoneNumber = "+1-555-0177"
        });
        response.EnsureSuccessStatusCode();

        using var body = await ReadJsonAsync(response);
        var token = body.RootElement.GetProperty("accessToken").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
