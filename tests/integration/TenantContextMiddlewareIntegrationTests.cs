using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using RestaurantOrder.Api.Tenancy;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying the TenantContextMiddleware, ProblemDetails error formats,
/// correlation ID propagation, development opt-in header resolution, and production header rejection.
/// </summary>
public class TenantContextMiddlewareIntegrationTests
{
    [Fact]
    public async Task Missing_TenantContext_On_Protected_Endpoint_Returns_ProblemDetails_With_CorrelationId()
    {
        using var factory = CreateCustomFactory(Environments.Development, allowDevHeaderOverride: false);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/test/tenant-scope");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Verify Correlation ID header
        Assert.True(response.Headers.Contains(TenantContextMiddleware.CorrelationIdHeader));
        var correlationId = response.Headers.GetValues(TenantContextMiddleware.CorrelationIdHeader).FirstOrDefault();
        Assert.False(string.IsNullOrWhiteSpace(correlationId));

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("https://restaurant-order.io/errors/tenant-context-required", json.GetProperty("type").GetString());
        Assert.Equal("Tenant Context Required", json.GetProperty("title").GetString());
        Assert.Equal(400, json.GetProperty("status").GetInt32());
        Assert.Equal("/api/v1/test/tenant-scope", json.GetProperty("instance").GetString());
        Assert.Equal(correlationId, json.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task Malformed_TenantHeader_In_Development_FailsClosed()
    {
        using var factory = CreateCustomFactory(Environments.Development, allowDevHeaderOverride: true);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope");
        request.Headers.Add("X-Tenant-Id", "not-a-valid-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ProductionEnvironment_Rejects_Untrusted_Header()
    {
        // In Production, plain headers are ignored by DefaultTenantContextResolver
        using var factory = CreateCustomFactory(Environments.Production, allowDevHeaderOverride: true);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        // Must fail closed because unverified headers are never trusted in Production
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DevelopmentEnvironment_With_OptIn_Accepts_Header()
    {
        using var factory = CreateCustomFactory(Environments.Development, allowDevHeaderOverride: true);
        using var client = factory.CreateClient();

        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Branch-Id", branchId.ToString());
        request.Headers.Add("X-Correlation-Id", "custom-corr-123");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("custom-corr-123", response.Headers.GetValues("X-Correlation-Id").First());

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(tenantId, json.GetProperty("tenantId").GetGuid());
        Assert.Equal(branchId, json.GetProperty("branchId").GetGuid());
        Assert.True(json.GetProperty("isAuthenticated").GetBoolean());
    }

    [Fact]
    public async Task SequentialRequests_DoNotLeakContext()
    {
        using var factory = CreateCustomFactory(Environments.Development, allowDevHeaderOverride: true);
        using var client = factory.CreateClient();

        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        // 1. Request with Tenant A
        using (var req1 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope"))
        {
            req1.Headers.Add("X-Tenant-Id", tenantA.ToString());
            var res1 = await client.SendAsync(req1);
            Assert.Equal(HttpStatusCode.OK, res1.StatusCode);
        }

        // 2. Request without header -> must return 400 (no leakage from Request 1)
        using (var req2 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope"))
        {
            var res2 = await client.SendAsync(req2);
            Assert.Equal(HttpStatusCode.BadRequest, res2.StatusCode);
        }

        // 3. Request with Tenant B
        using (var req3 = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope"))
        {
            req3.Headers.Add("X-Tenant-Id", tenantB.ToString());
            var res3 = await client.SendAsync(req3);
            Assert.Equal(HttpStatusCode.OK, res3.StatusCode);
            var json = await res3.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(tenantB, json.GetProperty("tenantId").GetGuid());
        }
    }

    [Fact]
    public async Task ParallelRequests_Maintain_TenantIsolation()
    {
        using var factory = CreateCustomFactory(Environments.Development, allowDevHeaderOverride: true);
        using var client = factory.CreateClient();

        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var expectedTenant = Guid.NewGuid();
            using var req = new HttpRequestMessage(HttpMethod.Get, "/api/v1/test/tenant-scope");
            req.Headers.Add("X-Tenant-Id", expectedTenant.ToString());

            var res = await client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);

            var json = await res.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(expectedTenant, json.GetProperty("tenantId").GetGuid());
        });

        await Task.WhenAll(tasks);
    }

    private static WebApplicationFactory<RestaurantOrder.Api.Program> CreateCustomFactory(
        string environment,
        bool allowDevHeaderOverride)
    {
        return new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("Tenancy:AllowDevHeaderOverride", allowDevHeaderOverride ? "true" : "false");
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("DATABASE_URL", "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass");
            builder.UseSetting("REDIS_URL", "localhost:6379");
            builder.UseSetting("JWT_SECRET", "super-secure-production-jwt-secret-key-32-chars-long!");

            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Tenancy:AllowDevHeaderOverride"] = allowDevHeaderOverride ? "true" : "false",
                    ["DEPLOYMENT_COLOR"] = "blue",
                    ["DATABASE_URL"] = "Host=localhost;Database=test;Username=test_app;Password=secret_app_pass",
                    ["REDIS_URL"] = "localhost:6379",
                    ["JWT_SECRET"] = "super-secure-production-jwt-secret-key-32-chars-long!"
                });
            });
        });
    }
}
