using System.Net;

namespace Northstar.Api.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class HealthAndSecurityTests(NorthstarApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Liveness_endpoint_returns_ok()
    {
        var response = await Client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Readiness_endpoint_reports_healthy_database()
    {
        var response = await Client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Security_headers_are_applied_to_every_response()
    {
        var response = await Client.GetAsync("/health/live");

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("frame-ancestors 'none'",
            response.Headers.GetValues("Content-Security-Policy").Single());
    }
}
