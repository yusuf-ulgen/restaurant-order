using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Catalog;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Catalog;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using StackExchange.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Integration tests verifying that customer session invalidation is resilient to Redis cache anomalies,
/// and that stale positive cache ("1") never authorizes closed or merged sessions.
/// </summary>
public class CustomerSessionCacheResilienceIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public CustomerSessionCacheResilienceIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken, Guid table1Id, Guid table2Id, Guid session1Id, Guid session2Id)> SetupFloorAndCatalogAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Resilience Tenant", $"res-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Resilience Brand", $"rb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Resilience Branch", $"rbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Resilience Area", $"ra-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "R-01", "Table 1", 4));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "R-02", "Table 2", 4));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var s1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{t1!.Id}/sessions", adminToken);
        s1Req.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(s1Req)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var s2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables/{t2!.Id}/sessions", adminToken);
        s2Req.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(s2Req)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Activate s1 and s2
        var act1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{s1!.Id}/activate", adminToken);
        act1Req.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        await client.SendAsync(act1Req);

        var act2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/sessions/{s2!.Id}/activate", adminToken);
        act2Req.Headers.Add("If-Match", $"\"{s2.ConcurrencyToken}\"");
        await client.SendAsync(act2Req);

        // Catalog menu for testing protected endpoints
        var menuReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus", adminToken);
        menuReq.Content = JsonContent.Create(new CreateMenuCommand("Main Menu", $"mm-{Guid.NewGuid():N}"));
        var menu = await (await client.SendAsync(menuReq)).Content.ReadFromJsonAsync<MenuDto>();

        var catReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus/{menu!.Id}/categories", adminToken);
        catReq.Content = JsonContent.Create(new CreateMenuCategoryCommand("Mains", $"cat-{Guid.NewGuid():N}"));
        var cat = await (await client.SendAsync(catReq)).Content.ReadFromJsonAsync<MenuCategoryDto>();

        var itemReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/catalog/branches/{branch.Id}/menus/{menu.Id}/items", adminToken);
        itemReq.Content = JsonContent.Create(new CreateMenuItemCommand(cat!.Id, "Kebab", $"kb-{Guid.NewGuid():N}", 2500));
        await client.SendAsync(itemReq);

        return (tenantId, branch.Id, adminToken, t1.Id, t2.Id, s1.Id, s2.Id);
    }

    [Fact]
    public async Task ClosedSession_WithStalePositiveRedisCache_StrictlyReturns401Unauthorized()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken, _, _, sessionId, _) = await SetupFloorAndCatalogAsync(client);

        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        // 1. Validate active session works and returns 200 OK
        var activeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var activeResp = await client.SendAsync(activeReq);
        Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);

        // 2. Close session via staff endpoint
        var sessionGetReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}", adminToken);
        var sessionDto = await (await client.SendAsync(sessionGetReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var closeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sessionId}/close", adminToken);
        closeReq.Headers.Add("If-Match", $"\"{sessionDto!.ConcurrencyToken}\"");
        closeReq.Content = JsonContent.Create(new CloseDiningSessionRequest("Guest left", sessionDto.ConcurrencyToken));
        var closeResp = await client.SendAsync(closeReq);
        Assert.Equal(HttpStatusCode.OK, closeResp.StatusCode);

        // 3. Force stale positive cache value "1" in Redis directly
        var redisKey = $"cs:{tenantId:N}:{branchId:N}:{sessionId:N}";
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var db = redis.GetDatabase();
        await db.StringSetAsync(redisKey, "1", TimeSpan.FromMinutes(5));

        // 4. Send request with customer JWT for closed session
        var closedReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var closedResp = await client.SendAsync(closedReq);

        // 5. Must strictly return 401 Unauthorized regardless of stale "1" in Redis
        Assert.Equal(HttpStatusCode.Unauthorized, closedResp.StatusCode);
    }

    [Fact]
    public async Task MergedSession_WithStalePositiveRedisCache_StrictlyReturns401Unauthorized()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken, _, _, sourceSessionId, targetSessionId) = await SetupFloorAndCatalogAsync(client);

        var sourceCustomerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sourceSessionId);

        // 1. Validate active source session works
        var activeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", sourceCustomerToken);
        var activeResp = await client.SendAsync(activeReq);
        Assert.Equal(HttpStatusCode.OK, activeResp.StatusCode);

        // Fetch fresh tokens for merge
        var s1Get = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{sourceSessionId}", adminToken));
        var s1Dto = await s1Get.Content.ReadFromJsonAsync<DiningSessionDto>();

        var s2Get = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{targetSessionId}", adminToken));
        var s2Dto = await s2Get.Content.ReadFromJsonAsync<DiningSessionDto>();

        // 2. Merge source session into target session
        var mergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sourceSessionId}/merge", adminToken);
        mergeReq.Headers.Add("If-Match", $"\"{s1Dto!.ConcurrencyToken}\"");
        mergeReq.Content = JsonContent.Create(new MergeSessionRequest(
            targetSessionId,
            s1Dto.ConcurrencyToken,
            s2Dto!.ConcurrencyToken,
            IdempotencyKey: $"merge-cache-{Guid.NewGuid():N}",
            Reason: "Merging party"));
        var mergeResp = await client.SendAsync(mergeReq);
        Assert.Equal(HttpStatusCode.OK, mergeResp.StatusCode);

        // 3. Force stale positive cache value "1" in Redis for source session
        var redisKey = $"cs:{tenantId:N}:{branchId:N}:{sourceSessionId:N}";
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_fixture.RedisEndpoint);
        var db = redis.GetDatabase();
        await db.StringSetAsync(redisKey, "1", TimeSpan.FromMinutes(5));

        // 4. Send request with customer JWT for merged source session
        var mergedReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", sourceCustomerToken);
        var mergedResp = await client.SendAsync(mergedReq);

        // 5. Must strictly return 401 Unauthorized regardless of stale "1" in Redis
        Assert.Equal(HttpStatusCode.Unauthorized, mergedResp.StatusCode);
    }
}
