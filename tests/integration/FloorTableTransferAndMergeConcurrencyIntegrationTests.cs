using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorTableTransferAndMergeConcurrencyIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorTableTransferAndMergeConcurrencyIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Conc Move Tenant", $"concmv-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Conc Brand", $"cb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Conc Branch", $"cbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"ca-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-301", "Table 301", 4));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-302", "Table 302", 6));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t3Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t3Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-303", "Table 303", 6));
        var t3 = await (await client.SendAsync(t3Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        return (tenantId, branch.Id, adminToken, t1!.Id, t2!.Id, t3!.Id);
    }

    [Fact]
    public async Task ParallelDuplicateIdempotency_BothSucceed_ExactlyOneRecordCreated()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"dup-par-{Guid.NewGuid():N}";

        // Send two identical transfer requests concurrently
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var taskA = Task.Run(async () =>
        {
            await barrier.Task;
            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
            req.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: key));
            return await client.SendAsync(req);
        });

        var taskB = Task.Run(async () =>
        {
            await barrier.Task;
            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
            req.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: key));
            return await client.SendAsync(req);
        });

        barrier.SetResult();
        var responses = await Task.WhenAll(taskA, taskB);

        // Neither request must fail with 500
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Exactly 1 movement record must be in the database
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tenancy.dining_session_movements WHERE idempotency_key = @k;";
        cmd.Parameters.AddWithValue("k", key);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task TransferAndMerge_ConcurrentRace_SerializesCleanlyWithout500()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, t3Id) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var o3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t3Id}/sessions", token);
        o3.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s3 = await (await client.SendAsync(o3)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Race: Transfer s1 -> T2 vs Merge s1 -> s3
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var transferTask = Task.Run(async () =>
        {
            await barrier.Task;
            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
            req.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"race-t-{Guid.NewGuid():N}"));
            return await client.SendAsync(req);
        });

        var mergeTask = Task.Run(async () =>
        {
            await barrier.Task;
            var req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/merge", token);
            req.Content = JsonContent.Create(new MergeSessionRequest(s3!.Id, s1.ConcurrencyToken, s3.ConcurrencyToken, IdempotencyKey: $"race-m-{Guid.NewGuid():N}"));
            return await client.SendAsync(req);
        });

        barrier.SetResult();
        var responses = await Task.WhenAll(transferTask, mergeTask);

        // Neither request must crash with 500
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));

        // At least one operation succeeds (200), the other may succeed or fail with conflict/bad request depending on serialization
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task CrossTenantAndCrossBranch_TransferAndMerge_RejectedCleanly()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tAId, bAId, tokenA, t1Id, _, _) = await SetupFloorAsync(client);
        var (tBId, bBId, tokenB, foreignTableId, _, _) = await SetupFloorAsync(client);

        var oA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{bAId}/tables/{t1Id}/sessions", tokenA);
        oA.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var sA = await (await client.SendAsync(oA)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // 1. Tenant B admin attempts to transfer Tenant A's session -> Forbidden / NotFound
        var crossTokenReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{bAId}/sessions/{sA!.Id}/transfer", tokenB);
        crossTokenReq.Content = JsonContent.Create(new TransferSessionRequest(foreignTableId, ConcurrencyToken: sA.ConcurrencyToken, IdempotencyKey: $"ct-1-{Guid.NewGuid():N}"));
        var crossTokenRes = await client.SendAsync(crossTokenReq);
        Assert.True(crossTokenRes.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound);

        // 2. Transfer session to a table from another tenant/branch -> 404 / 400
        var crossTableReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{bAId}/sessions/{sA.Id}/transfer", tokenA);
        crossTableReq.Content = JsonContent.Create(new TransferSessionRequest(foreignTableId, ConcurrencyToken: sA.ConcurrencyToken, IdempotencyKey: $"ct-2-{Guid.NewGuid():N}"));
        var crossTableRes = await client.SendAsync(crossTableReq);
        Assert.True(crossTableRes.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task QrExchange_ConcurrentWithDeactivationOrRotation_IsSerializedSafely()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, adminToken, tableId, _, _) = await SetupFloorAsync(client);

        // Get QR code token for table
        var qrResp = await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
            HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/qr", adminToken));
        var qrMeta = await qrResp.Content.ReadFromJsonAsync<TableQrCodeDto>(JsonOptions);
        Assert.NotNull(qrMeta?.Token);

        // Concurrently run QR exchange and QR rotation
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exchangeTask = Task.Run(async () =>
        {
            await barrier.Task;
            return await client.PostAsJsonAsync("/api/v1/qr/exchange", new QrExchangeRequest(qrMeta.Token));
        });

        var rotateTask = Task.Run(async () =>
        {
            await barrier.Task;
            return await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(
                HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{tableId}/qr/rotate", adminToken));
        });

        barrier.SetResult();
        var responses = await Task.WhenAll(exchangeTask, rotateTask);

        // Neither request must fail with 500
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
    }
}
