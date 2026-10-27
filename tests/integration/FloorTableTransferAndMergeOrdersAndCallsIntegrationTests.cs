using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.Orders;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Common;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Domain.Orders;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorTableTransferAndMergeOrdersAndCallsIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorTableTransferAndMergeOrdersAndCallsIntegrationTests(TestcontainersFixture fixture)
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

    private async Task<(Guid tenantId, Guid branchId, string adminToken, Guid t1Id, Guid t2Id, Guid t3Id)> SetupFloorAsync(HttpClient client)
    {
        var tenantId = Guid.NewGuid();
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "OrdersMove Tenant", $"om-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Move Brand", $"mb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Move Branch", $"mbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"ca-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-201", "Table 201", 6));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-202", "Table 202", 8));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t3Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t3Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-203", "Table 203", 6));
        var t3 = await (await client.SendAsync(t3Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        return (tenantId, branch.Id, adminToken, t1!.Id, t2!.Id, t3!.Id);
    }

    [Fact]
    public async Task Transfer_WithOrdersAndCalls_UpdatesOrderTableAndServiceRequestTable()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        // 1. Open session at Table 1
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(session);

        // Seed a direct order and service request for this session
        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var db = new RestaurantOrderDbContext(options);

        var orderId = OrderId.New();
        var item1 = OrderItem.Create(
            orderId,
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Catalog.MenuItemId(Guid.NewGuid()),
            null,
            "Adana Kebap",
            null,
            1,
            25000L,
            2000);
        item1.StartPreparation();
        item1.MarkReady();

        var order = Order.CreateSubmitted(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(session.Id),
            new Domain.Floor.RestaurantTableId(t1Id),
            1,
            OrderSource.Staff,
            Currency.Default,
            $"key-trans-{Guid.NewGuid():N}",
            new[] { item1 });
        db.Orders.Add(order);

        var sRequest = Domain.Floor.ServiceRequest.Create(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(session.Id),
            new Domain.Floor.RestaurantTableId(t1Id),
            Domain.Floor.ServiceRequestType.Waiter,
            "Su rica ediyoruz",
            $"sr-trans-{Guid.NewGuid():N}");
        db.ServiceRequests.Add(sRequest);
        await db.SaveChangesAsync();

        // 2. Transfer session from Table 1 to Table 2
        var idempotencyKey = $"idem-order-trans-{Guid.NewGuid():N}";
        var transferReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        transferReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        transferReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: session.ConcurrencyToken, IdempotencyKey: idempotencyKey));

        var transferRes = await client.SendAsync(transferReq);
        Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);

        // 3. Verify order and service request table reference was updated in DB
        await using var verifyDb = new RestaurantOrderDbContext(options);
        var updatedOrder = await verifyDb.Orders.FirstAsync(o => o.Id == order.Id);
        Assert.Equal(t2Id, updatedOrder.TableId.Value);

        var updatedSr = await verifyDb.ServiceRequests.FirstAsync(sr => sr.Id == sRequest.Id);
        Assert.Equal(t2Id, updatedSr.TableId.Value);

        // 4. Verify ready items API resolves the item under Table 2
        var readyReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/orders/ready-items?tableId={t2Id}", token);
        var readyRes = await client.SendAsync(readyReq);
        Assert.Equal(HttpStatusCode.OK, readyRes.StatusCode);
        var readyItems = await readyRes.Content.ReadFromJsonAsync<List<ReadyOrderItemDto>>();
        Assert.NotNull(readyItems);
        Assert.Contains(readyItems, item => item.ItemId == order.Items.First().Id.Value);
    }

    [Fact]
    public async Task Merge_WithOrdersAndCalls_ReassignsOrdersWithSequentialRounds_AndReassignsCalls()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        // 1. Open Source Session at Table 1
        var open1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        open1Req.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var sourceSession = await (await client.SendAsync(open1Req)).Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(sourceSession);

        // 2. Open Target Session at Table 2
        var open2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        open2Req.Content = JsonContent.Create(new OpenDiningSessionRequest(3));
        var targetSession = await (await client.SendAsync(open2Req)).Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(targetSession);

        var options = new DbContextOptionsBuilder<RestaurantOrderDbContext>()
            .UseNpgsql(_fixture.DatabaseConnectionString)
            .Options;
        await using var db = new RestaurantOrderDbContext(options);

        // Target session already has Round 1
        var tOrd1Id = OrderId.New();
        var tItem1 = OrderItem.Create(
            tOrd1Id,
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Catalog.MenuItemId(Guid.NewGuid()),
            null,
            "Ayran",
            null,
            2,
            3000L,
            200);

        var targetOrder1 = Order.CreateSubmitted(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(targetSession.Id),
            new Domain.Floor.RestaurantTableId(t2Id),
            1,
            OrderSource.Staff,
            Currency.Default,
            $"key-target-1-{Guid.NewGuid():N}",
            new[] { tItem1 });
        db.Orders.Add(targetOrder1);

        // Source session also has Round 1 and Round 2
        var sOrd1Id = OrderId.New();
        var sItem1 = OrderItem.Create(
            sOrd1Id,
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Catalog.MenuItemId(Guid.NewGuid()),
            null,
            "Lahmacun",
            null,
            3,
            12000L,
            1000);

        var sourceOrder1 = Order.CreateSubmitted(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(sourceSession.Id),
            new Domain.Floor.RestaurantTableId(t1Id),
            1,
            OrderSource.Staff,
            Currency.Default,
            $"key-source-1-{Guid.NewGuid():N}",
            new[] { sItem1 });

        var sOrd2Id = OrderId.New();
        var sItem2 = OrderItem.Create(
            sOrd2Id,
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Catalog.MenuItemId(Guid.NewGuid()),
            null,
            "Künefe",
            null,
            1,
            18000L,
            1500);

        var sourceOrder2 = Order.CreateSubmitted(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(sourceSession.Id),
            new Domain.Floor.RestaurantTableId(t1Id),
            2,
            OrderSource.Staff,
            Currency.Default,
            $"key-source-2-{Guid.NewGuid():N}",
            new[] { sItem2 });

        db.Orders.AddRange(sourceOrder1, sourceOrder2);

        var sourceCall = Domain.Floor.ServiceRequest.Create(
            new Domain.Tenants.TenantId(tenantId),
            new Domain.Branches.BranchId(branchId),
            new Domain.Floor.DiningSessionId(sourceSession.Id),
            new Domain.Floor.RestaurantTableId(t1Id),
            Domain.Floor.ServiceRequestType.Napkin,
            "Peçete rica ediyoruz",
            $"sr-merge-{Guid.NewGuid():N}");
        db.ServiceRequests.Add(sourceCall);
        await db.SaveChangesAsync();

        // 3. Merge Source into Target
        var idempotencyKey = $"idem-merge-orders-{Guid.NewGuid():N}";
        var mergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{sourceSession.Id}/merge", token);
        mergeReq.Headers.Add("If-Match", $"\"{sourceSession.ConcurrencyToken}\"");
        mergeReq.Content = JsonContent.Create(new MergeSessionRequest(
            targetSession.Id,
            SourceConcurrencyToken: sourceSession.ConcurrencyToken,
            TargetConcurrencyToken: targetSession.ConcurrencyToken,
            IdempotencyKey: idempotencyKey,
            Reason: "Masaları birleştiriyoruz"));

        var mergeRes = await client.SendAsync(mergeReq);
        Assert.Equal(HttpStatusCode.OK, mergeRes.StatusCode);

        // 4. Verify orders in DB after merge:
        // No unique constraint violation occurred!
        // Target session now has 3 orders: original Round 1, sourceOrder1 as Round 2, sourceOrder2 as Round 3
        await using var verifyDb = new RestaurantOrderDbContext(options);
        var ordersInTargetSession = await verifyDb.Orders
            .Where(o => o.TenantId == new Domain.Tenants.TenantId(tenantId) &&
                        o.DiningSessionId == new Domain.Floor.DiningSessionId(targetSession.Id))
            .OrderBy(o => o.RoundNumber)
            .ToListAsync();

        Assert.Equal(3, ordersInTargetSession.Count);
        Assert.Equal(1, ordersInTargetSession[0].RoundNumber);
        Assert.Equal(targetOrder1.Id, ordersInTargetSession[0].Id);

        Assert.Equal(2, ordersInTargetSession[1].RoundNumber);
        Assert.Equal(sourceOrder1.Id, ordersInTargetSession[1].Id);
        Assert.Equal(t2Id, ordersInTargetSession[1].TableId.Value);

        Assert.Equal(3, ordersInTargetSession[2].RoundNumber);
        Assert.Equal(sourceOrder2.Id, ordersInTargetSession[2].Id);
        Assert.Equal(t2Id, ordersInTargetSession[2].TableId.Value);

        // Verify active service request from source was moved to target
        var reassignedSr = await verifyDb.ServiceRequests.FirstAsync(sr => sr.Id == sourceCall.Id);
        Assert.Equal(targetSession.Id, reassignedSr.DiningSessionId.Value);
        Assert.Equal(t2Id, reassignedSr.TableId.Value);

        // Verify source session is closed
        var closedSource = await verifyDb.DiningSessions.FirstAsync(s => s.Id == new Domain.Floor.DiningSessionId(sourceSession.Id));
        Assert.Equal(DiningSessionStatus.Closed, closedSource.Status);
    }
}
