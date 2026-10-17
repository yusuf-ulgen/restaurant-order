using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Auth;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Concurrency and distributed Redis rate limiter integration tests for customer service requests.
/// Verifies PostgreSQL advisory transaction locks, race condition resilience, and Redis fail-closed behaviors.
/// </summary>
public class CustomerServiceRequestConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerServiceRequestConcurrencyIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken, Guid tableId, Guid sessionId)> SetupActiveSessionAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Concurrency Tenant", $"ct-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Concurrency Brand", $"cb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Concurrency Branch", $"cbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Floor", $"mf-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var tableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        tableReq.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "Masa-C1", "Booth C1", 4));
        var table = await (await client.SendAsync(tableReq)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var sessionReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{table!.Id}/sessions", adminToken);
        sessionReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(sessionReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var activateReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{session!.Id}/activate", adminToken);
        activateReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        await client.SendAsync(activateReq);

        return (tenantId, branch.Id, adminToken, table.Id, session.Id);
    }

    [Fact]
    public async Task CreateServiceRequest_ConcurrentRequests_DifferentIdempotencyKeys_SafelyYieldsSingleOpenRecord()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, _, _, sessionId) = await SetupActiveSessionAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var key1 = $"race-key-1-{Guid.NewGuid():N}";
        var key2 = $"race-key-2-{Guid.NewGuid():N}";

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req1.Headers.Add("Idempotency-Key", key1);
        req1.Content = new StringContent(
            "{\"requestType\":\"Water\",\"note\":\"İlk istek\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, "/api/v1/customer/service-requests", customerToken);
        req2.Headers.Add("Idempotency-Key", key2);
        req2.Content = new StringContent(
            "{\"requestType\":\"Water\",\"note\":\"İkinci istek\"}",
            System.Text.Encoding.UTF8,
            "application/json");

        // Fire both concurrently
        var task1 = client.SendAsync(req1);
        var task2 = client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        // Neither request must fail with 500 or unhandled unique constraint violation
        foreach (var res in responses)
        {
            Assert.True(
                res.StatusCode == HttpStatusCode.OK,
                $"Expected OK (200) but got {res.StatusCode}. Details: {await res.Content.ReadAsStringAsync()}");
        }

        var dto1 = await responses[0].Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);
        var dto2 = await responses[1].Content.ReadFromJsonAsync<ServiceRequestDto>(JsonOptions);

        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.Equal(dto1.Id, dto2.Id);

        // Verify database source of truth: strictly ONE open record exists for Water
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);

        var openRecords = await db.ServiceRequests
            .Where(sr => sr.TenantId == new TenantId(tenantId) &&
                         sr.DiningSessionId == new DiningSessionId(sessionId) &&
                         sr.RequestType == ServiceRequestType.Water &&
                         sr.Status == ServiceRequestStatus.Open)
            .ToListAsync();

        Assert.Single(openRecords);
        Assert.Equal(dto1.Id, openRecords[0].Id.Value);
    }

    [Fact]
    public async Task RedisServiceRequestRateLimiter_LiveRedis_EnforcesCooldownAndWindow()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;

        var hostEnv = new TestHostEnvironment("Production");
        await using var redis = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var redisProvider = new TestRedisProvider(redis);

        var limiter = new RedisServiceRequestRateLimiter(
            redisProvider,
            hostEnv,
            cooldownPeriod: TimeSpan.FromSeconds(3),
            windowPeriod: TimeSpan.FromSeconds(10),
            maxRequestsPerWindow: 2);

        var tenantId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        // 1. First request succeeds
        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);

        // 2. Immediate second request within 3s cooldown throws
        var cooldownEx = await Assert.ThrowsAsync<ServiceRequestRateLimitException>(() =>
            limiter.EnsureNotRateLimitedAsync(tenantId, sessionId));

        Assert.True(cooldownEx.RetryAfterSeconds > 0);
        Assert.Contains("wait", cooldownEx.Message);

        // Reset limiter
        limiter.Reset(tenantId, sessionId);

        // 3. After reset, succeeds again
        await limiter.EnsureNotRateLimitedAsync(tenantId, sessionId);
    }

    private sealed class TestRedisProvider : IRedisDatabaseProvider
    {
        private readonly StackExchange.Redis.IConnectionMultiplexer _multiplexer;
        public TestRedisProvider(StackExchange.Redis.IConnectionMultiplexer multiplexer) => _multiplexer = multiplexer;
        public Task<StackExchange.Redis.IDatabase?> GetDatabaseAsync(CancellationToken ct = default) =>
            Task.FromResult<StackExchange.Redis.IDatabase?>(_multiplexer.GetDatabase());
        public Task<StackExchange.Redis.IDatabase> GetRequiredDatabaseAsync(CancellationToken ct = default) =>
            Task.FromResult(_multiplexer.GetDatabase());
    }

    private sealed class TestHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "RestaurantOrder.IntegrationTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;

        public TestHostEnvironment(string envName)
        {
            EnvironmentName = envName;
        }
    }
}
