using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ExamKiosk.Web.Tests;

public sealed class HealthEndpointTests
{
    [Fact]
    public async Task Health_ReturnsHealthyStatus()
    {
        await using var application = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(HomePageTests.ConfigureTestEntraSettings);
        using var client = application.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("healthy", payload?.Status);
    }

    private sealed record HealthResponse(string Status);
}