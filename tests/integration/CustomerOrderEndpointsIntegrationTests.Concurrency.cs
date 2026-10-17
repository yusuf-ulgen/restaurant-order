using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Npgsql;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Application.Auth;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Domain.Auth;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Domain.Tenants;
using RestaurantOrder.Infrastructure.Persistence;
using RestaurantOrder.Infrastructure.Redis;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

/// <summary>
/// Concurrency, RLS isolation, and infrastructure resilience tests for Phase 8.3 customer orders.
/// </summary>
public partial class CustomerOrderEndpointsIntegrationTests
{
    [Fact]
    public async Task CreateCustomerOrder_ConcurrentRequests_SameSession_AssignsIncrementalRoundNumbers()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req1.Headers.Add("Idempotency-Key", $"conc-a-{Guid.NewGuid():N}");
        req1.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, "Round A", null, 2500)
        }));

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req2.Headers.Add("Idempotency-Key", $"conc-b-{Guid.NewGuid():N}");
        req2.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 2, "Round B", null, 2500)
        }));

        var task1 = client.SendAsync(req1);
        var task2 = client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        Assert.Equal(HttpStatusCode.Created, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.Created, responses[1].StatusCode);

        var dto1 = await responses[0].Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);
        var dto2 = await responses[1].Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);

        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.NotEqual(dto1.Id, dto2.Id);

        var roundNumbers = new HashSet<int> { dto1.RoundNumber, dto2.RoundNumber };
        Assert.Contains(1, roundNumbers);
        Assert.Contains(2, roundNumbers);

        // Verify in DB that two separate orders exist with rounds 1 and 2
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);
        var dbOrders = await db.Set<Order>()
            .IgnoreQueryFilters()
            .Where(o => o.DiningSessionId == new DiningSessionId(sessionId))
            .OrderBy(o => o.RoundNumber)
            .ToListAsync();

        Assert.Equal(2, dbOrders.Count);
        Assert.Equal(1, dbOrders[0].RoundNumber);
        Assert.Equal(2, dbOrders[1].RoundNumber);
    }

    [Fact]
    public async Task CreateCustomerOrder_ConcurrentRequests_SameKey_SingleOrderPersisted()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);
        var key = $"race-key-{Guid.NewGuid():N}";

        var requestBody = new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, "Concurrent race", null, 2500)
        });

        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req1.Headers.Add("Idempotency-Key", key);
        req1.Content = JsonContent.Create(requestBody);

        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req2.Headers.Add("Idempotency-Key", key);
        req2.Content = JsonContent.Create(requestBody);

        var task1 = client.SendAsync(req1);
        var task2 = client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.All(statusCodes, sc => Assert.True(sc == HttpStatusCode.Created || sc == HttpStatusCode.OK));

        var dto1 = await responses[0].Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);
        var dto2 = await responses[1].Content.ReadFromJsonAsync<CustomerOrderResponseDto>(JsonOptions);

        Assert.NotNull(dto1);
        Assert.NotNull(dto2);
        Assert.Equal(dto1.Id, dto2.Id);

        // Verify exactly one order exists in database for this key
        var dbOptions = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var db = new RestaurantOrderDbContext(dbOptions);
        var count = await db.Set<Order>()
            .IgnoreQueryFilters()
            .CountAsync(o => o.IdempotencyKey == key);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PostgreSql_Rls_OrderIsolation_DirectDbQuery()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId1, branchId1, _, sessionId1, menuItemId1) = await SetupSessionAndCatalogAsync();
        var (tenantId2, _, _, _, _) = await SetupSessionAndCatalogAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId1, branchId1, sessionId1);

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"rls-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId1, null, 1, null, null, 2500)
        }));
        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);

        // Verify RLS isolation using temporary runtime role
        var runtimeConnString = await _fixture.ProvisionTemporaryRuntimeRoleAsync();
        await using var conn = new NpgsqlConnection(runtimeConnString);
        await conn.OpenAsync();

        // 1. In context of Tenant 1: order is visible
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tId, false); SELECT COUNT(*) FROM tenancy.orders;";
            cmd.Parameters.AddWithValue("tId", tenantId1.ToString());
            var count1 = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.True(count1 >= 1);
        }

        // 2. In context of Tenant 2: order from Tenant 1 is NOT visible
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tId, false); SELECT COUNT(*) FROM tenancy.orders;";
            cmd.Parameters.AddWithValue("tId", tenantId2.ToString());
            var count2 = Convert.ToInt32(await cmd.ExecuteScalarAsync());
            Assert.Equal(0, count2);
        }
    }

    [Fact]
    public async Task CreateCustomerOrder_WhenSecurityStateUnavailable_Returns503WithoutLeakingDetails()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var (tenantId, branchId, _, sessionId, menuItemId) = await SetupSessionAndCatalogAsync();
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, sessionId);

        var sensitiveErrorMessage = "CRITICAL: Redis connection refused on redis-cluster-private-node-99.internal:6379 with auth pass ***";
        var mockOrderService = new Mock<ICustomerOrderService>();
        mockOrderService
            .Setup(s => s.CreateOrderAsync(
                It.IsAny<TenantId>(),
                It.IsAny<RestaurantOrder.Domain.Branches.BranchId>(),
                It.IsAny<DiningSessionId>(),
                It.IsAny<CreateCustomerOrderRequest>(),
                It.IsAny<string>(),
                It.IsAny<AuthenticatedPrincipal>(),
                It.IsAny<CancellationToken>()))
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
                services.AddScoped(_ => mockOrderService.Object);
            });
        });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/customer/orders", customerToken);
        req.Headers.Add("Idempotency-Key", $"sec-key-{Guid.NewGuid():N}");
        req.Content = JsonContent.Create(new CreateCustomerOrderRequest(new List<CustomerOrderItemRequest>
        {
            new(menuItemId, null, 1, null, null, 2500)
        }));

        var res = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, res.StatusCode);

        var body = await res.Content.ReadAsStringAsync();
        Assert.Contains("DISTRIBUTED_SECURITY_STATE_UNAVAILABLE", body);
        Assert.DoesNotContain("redis-cluster-private-node-99", body);
        Assert.DoesNotContain("auth pass ***", body);
    }
}
