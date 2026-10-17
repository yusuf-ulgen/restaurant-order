using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying that when distributed security / rate limit state (Redis)
/// is unavailable in Staging/Production fail-closed mode, endpoints return RFC 7807 503 Service Unavailable,
/// preserve correlation ID, mask sensitive internal infrastructure details, and maintain fail-closed security.
/// </summary>
public class CustomerServiceRequestSecurityStateUnavailableIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerServiceRequestSecurityStateUnavailableIntegrationTests(TestcontainersFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task EnsureMigrationsAppliedAsync()
    {
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(RestaurantOrderDbContext).Assembly.FullName);
            })
            .Options;

        await using var context = new RestaurantOrderDbContext(options);
        await context.Database.MigrateAsync();
    }

    private async Task<(Guid tenantId, Guid branchId, Guid sessionId)> SetupActiveSessionAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Sec State Tenant", $"sst-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("SS Brand", $"ssb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "SS Branch", $"ssbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Floor", $"mf-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "Masa-9", "Booth 9", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        sessionReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(sessionReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{session!.Id}/activate", adminToken);
        activateReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        await client.SendAsync(activateReq);

        return (tenantId, branch.Id, session.Id);
    }

    [Fact]
    public async Task CreateServiceRequest_WhenRateLimiterThrowsDistributedSecurityStateUnavailable_Returns503WithoutLeakingDetails()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var standardClient = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, sessionId) = await SetupActiveSessionAsync(standardClient);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var sensitiveErrorMessage = "CRITICAL: Redis connection refused on redis-cluster-private-node-99.internal:6379 with auth pass ***";
        var mockLimiter = new Mock<IServiceRequestRateLimiter>();
        mockLimiter
            .Setup(l => l.EnsureNotRateLimitedAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DistributedSecurityStateUnavailableException(sensitiveErrorMessage));

        var factory = new WebApplicationFactory<RestaurantOrder.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:Database", _fixture.DatabaseConnectionString);
            builder.UseSetting("DATABASE_URL", _fixture.DatabaseConnectionString);
            builder.UseSetting("REDIS_URL", _fixture.RedisEndpoint);
            builder.UseSetting("JWT_SECRET", RestaurantOrder.Api.ConfigurationValidator.InsecureDevJwtSecret);
            builder.UseSetting("DEPLOYMENT_COLOR", "blue");
            builder.UseSetting("QrSecurity:CurrentKeyId", "k1");
            builder.UseSetting("QrSecurity:Keys:k1", RestaurantConfigTestHelpers.SharedTestQrKeyBase64);
            builder.ConfigureServices(services =>
            {
                var mockValidator = new Mock<ITokenRevocationValidator>();
                mockValidator.Setup(v => v.ValidateTokenActiveAsync(
                        It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(true);
                services.AddScoped(_ => mockValidator.Object);
                services.AddScoped<IServiceRequestRateLimiter>(_ => mockLimiter.Object);
            });
        });

        var client = factory.CreateClient();
        var correlationId = Guid.NewGuid().ToString("D");
        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req.Headers.Add("Idempotency-Key", $"idem-sec-{Guid.NewGuid():N}");
        req.Headers.Add("X-Correlation-Id", correlationId);
        req.Content = new StringContent(
            "{\"requestType\":\"Waiter\",\"note\":\"Test rate limit unavailable\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var response = await client.SendAsync(req);

        // 1. HTTP Status must be 503 Service Unavailable (NOT 500)
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        // 2. Content-Type must be application/problem+json
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // 3. Response body must not contain internal Redis credentials or host details
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("redis-cluster-private-node-99", body);
        Assert.DoesNotContain("6379", body);
        Assert.DoesNotContain("CRITICAL", body);

        // 4. RFC 7807 ProblemDetails fields
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        Assert.Equal(503, root.GetProperty("status").GetInt32());
        Assert.Equal("Service Unavailable", root.GetProperty("title").GetString());
        Assert.Equal("https://httpstatuses.com/503", root.GetProperty("type").GetString());
        Assert.Contains("temporarily unavailable", root.GetProperty("detail").GetString()!);
        Assert.Equal(correlationId, root.GetProperty("correlationId").GetString());
    }
}
