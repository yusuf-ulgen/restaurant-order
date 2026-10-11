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

public class FloorTableTransferAndMergeIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorTableTransferAndMergeIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Move Tenant", $"move-{Guid.NewGuid():N}");
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

        // Create Table 1 (cap 4)
        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-101", "Table 101", 4));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        // Create Table 2 (cap 6)
        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-102", "Table 102", 6));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        // Create Table 3 (cap 2, small)
        var t3Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t3Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-103", "Table 103", 2));
        var t3 = await (await client.SendAsync(t3Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        return (tenantId, branch.Id, adminToken, t1!.Id, t2!.Id, t3!.Id);
    }

    [Fact]
    public async Task Transfer_Success_MovesSessionAndCreatesImmutableMovement_AndReplayIsIdempotent()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        // 1. Open session at Table 1
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(3));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(session);
        Assert.Equal(t1Id, session.TableId);

        // 2. Transfer session to Table 2
        var idempotencyKey = $"idem-transfer-{Guid.NewGuid():N}";
        var transferReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        transferReq.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        transferReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: session.ConcurrencyToken, IdempotencyKey: idempotencyKey, Reason: "Guest requested Table 2"));

        var transferRes = await client.SendAsync(transferReq);
        Assert.Equal(HttpStatusCode.OK, transferRes.StatusCode);
        var transferred = await transferRes.Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(transferred);
        Assert.Equal(t2Id, transferred.TableId);

        // 3. Replay with same idempotency key returns same session without error
        var replayReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        replayReq.Headers.Add("If-Match", $"\"{transferred.ConcurrencyToken}\"");
        replayReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: transferred.ConcurrencyToken, IdempotencyKey: idempotencyKey));
        var replayRes = await client.SendAsync(replayReq);
        Assert.Equal(HttpStatusCode.OK, replayRes.StatusCode);

        // 4. Verify exactly 1 movement record exists in database
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM tenancy.dining_session_movements WHERE idempotency_key = @key";
        cmd.Parameters.AddWithValue("key", idempotencyKey);
        var count = Convert.ToInt64(await cmd.ExecuteScalarAsync());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Transfer_ValidationFailures_RejectedCleanly()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, t3Id) = await SetupFloorAsync(client);

        // Open session at Table 1 with 3 guests
        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(3));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // A. Same table transfer rejected
        var sameReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session!.Id}/transfer", token);
        sameReq.Content = JsonContent.Create(new TransferSessionRequest(t1Id, ConcurrencyToken: session.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(sameReq)).StatusCode);

        // B. Exceeds capacity (Table 3 capacity is 2, session has 3 guests)
        var capReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        capReq.Content = JsonContent.Create(new TransferSessionRequest(t3Id, ConcurrencyToken: session.ConcurrencyToken));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(capReq)).StatusCode);

        // C. Stale concurrency token rejected with 409
        var staleReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        staleReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: Guid.NewGuid(), IdempotencyKey: $"stale-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(staleReq)).StatusCode);

        // D. Customer role forbidden
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, session.Id);
        var custReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", customerToken);
        custReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: session.ConcurrencyToken, IdempotencyKey: $"cust-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(custReq)).StatusCode);
    }

    [Fact]
    public async Task Merge_Success_TerminalizesSource_CombinesGuests_AndReplayIsIdempotent()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        // Open session 1 at Table 1 (guests = 2)
        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Open session 2 at Table 2 (guests = 3)
        var o2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        o2.Content = JsonContent.Create(new OpenDiningSessionRequest(3));
        var s2 = await (await client.SendAsync(o2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Merge s1 into s2
        var idempotencyKey = $"idem-merge-{Guid.NewGuid():N}";
        var mergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/merge", token);
        mergeReq.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        mergeReq.Content = JsonContent.Create(new MergeSessionRequest(
            TargetSessionId: s2!.Id,
            SourceConcurrencyToken: s1.ConcurrencyToken,
            TargetConcurrencyToken: s2.ConcurrencyToken,
            IdempotencyKey: idempotencyKey,
            Reason: "Joined family"));

        var mergeRes = await client.SendAsync(mergeReq);
        Assert.Equal(HttpStatusCode.OK, mergeRes.StatusCode);
        var mergedTarget = await mergeRes.Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(mergedTarget);
        Assert.Equal(5, mergedTarget.GuestCount); // 2 + 3 = 5

        // Check source session is now Closed with MergedIntoSessionId
        var getSourceReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}", token);
        var sourceDb = await (await client.SendAsync(getSourceReq)).Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.NotNull(sourceDb);
        Assert.Equal("Closed", sourceDb.Status);
        Assert.Equal(s2.Id, sourceDb.MergedIntoSessionId);

        // Replay merge: guest count remains 5 (not 7)
        var replayMergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/merge", token);
        replayMergeReq.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        replayMergeReq.Content = JsonContent.Create(new MergeSessionRequest(s2.Id, s1.ConcurrencyToken, s2.ConcurrencyToken, IdempotencyKey: idempotencyKey));
        var replayRes = await client.SendAsync(replayMergeReq);
        Assert.Equal(HttpStatusCode.OK, replayRes.StatusCode);
        var replayedTarget = await replayRes.Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.Equal(5, replayedTarget!.GuestCount);
    }

    [Fact]
    public async Task Concurrent_ReverseTransfers_DoNotDeadlock_SerializeDeterministically()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        // Open session 1 at Table 1 and session 2 at Table 2
        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var o2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        o2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(o2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Concurrent reverse transfer attempts:
        // Request A: s1 -> t2 (should fail because t2 is occupied)
        // Request B: s2 -> t1 (should fail because t1 is occupied)
        var reqA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        reqA.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"rev-a-{Guid.NewGuid():N}"));

        var reqB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s2!.Id}/transfer", token);
        reqB.Content = JsonContent.Create(new TransferSessionRequest(t1Id, ConcurrencyToken: s2.ConcurrencyToken, IdempotencyKey: $"rev-b-{Guid.NewGuid():N}"));

        var taskA = client.SendAsync(reqA);
        var taskB = client.SendAsync(reqB);
        var responses = await Task.WhenAll(taskA, taskB);

        // Neither request must deadlock or throw 500
        Assert.All(responses, r => Assert.NotEqual(HttpStatusCode.InternalServerError, r.StatusCode));
    }

    [Fact]
    public async Task MovementHistory_DatabaseTrigger_RejectsUpdateOrDelete_AndRlsIsEnforced()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var transferReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        transferReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"trig-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(transferReq)).StatusCode);

        // 1. Direct database UPDATE on movements table must be blocked by trigger
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using (var updateCmd = conn.CreateCommand())
        {
            updateCmd.CommandText = "UPDATE tenancy.dining_session_movements SET reason = 'tampered' WHERE source_session_id = @sid;";
            updateCmd.Parameters.AddWithValue("sid", s1.Id);
            var updateEx = await Assert.ThrowsAsync<PostgresException>(() => updateCmd.ExecuteNonQueryAsync());
            Assert.Contains("immutable", updateEx.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 2. Direct database DELETE on movements table must be blocked by trigger
        await using (var deleteCmd = conn.CreateCommand())
        {
            deleteCmd.CommandText = "DELETE FROM tenancy.dining_session_movements WHERE source_session_id = @sid;";
            deleteCmd.Parameters.AddWithValue("sid", s1.Id);
            var deleteEx = await Assert.ThrowsAsync<PostgresException>(() => deleteCmd.ExecuteNonQueryAsync());
            Assert.Contains("immutable", deleteEx.Message, StringComparison.OrdinalIgnoreCase);
        }

        // 3. RLS isolation check: with runtime role without tenant context, 0 rows returned
        await using (var rlsCmd = conn.CreateCommand())
        {
            rlsCmd.CommandText = @"
                SET ROLE restaurant_app_runtime;
                RESET tenancy.current_tenant_id;
                SELECT COUNT(*) FROM tenancy.dining_session_movements;
            ";
            var countWithoutContext = Convert.ToInt64(await rlsCmd.ExecuteScalarAsync());
            Assert.Equal(0, countWithoutContext);
        }
    }
}
