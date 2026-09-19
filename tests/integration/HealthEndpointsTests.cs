using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

using RestaurantOrder.Api;

namespace RestaurantOrder.IntegrationTests;

public class HealthEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthLive_ReturnsOk_WithHealthyStatus()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.Equal("restaurant-order-api", content.GetProperty("service").GetString());
        Assert.Equal("0.1.0", content.GetProperty("version").GetString());
    }

    [Fact]
    public async Task HealthReady_ReturnsOk_WithHealthyStatus()
    {
        var response = await _client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Healthy", content.GetProperty("status").GetString());
        Assert.Equal("restaurant-order-api", content.GetProperty("service").GetString());
        Assert.Equal("0.1.0", content.GetProperty("version").GetString());
    }
}
