using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RestaurantOrder.Api.Floor;
using RestaurantOrder.Api.RestaurantConfig;
using RestaurantOrder.Application.Floor;
using RestaurantOrder.Application.RestaurantConfig;
using RestaurantOrder.Domain.Branches;
using RestaurantOrder.Domain.Floor;
using RestaurantOrder.Infrastructure.Persistence;
using Xunit;

namespace RestaurantOrder.IntegrationTests;

public class FloorTableTransferAndMergeSecurityIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorTableTransferAndMergeSecurityIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "Sec Move Tenant", $"secmv-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("Sec Brand", $"sb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "Sec Branch", $"sbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Hall", $"ca-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-201", "Table 201", 4));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-202", "Table 202", 6));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t3Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t3Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-203", "Table 203", 4));
        var t3 = await (await client.SendAsync(t3Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        return (tenantId, branch.Id, adminToken, t1!.Id, t2!.Id, t3!.Id);
    }

    [Fact]
    public async Task TransferAndMerge_MissingOrInvalidIdempotencyKey_ReturnsProblemDetails400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // 1. Missing idempotency key on transfer
        var noKeyReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        noKeyReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: null!));
        var noKeyResp = await client.SendAsync(noKeyReq);
        Assert.Equal(HttpStatusCode.BadRequest, noKeyResp.StatusCode);
        Assert.Contains("problem+json", noKeyResp.Content.Headers.ContentType?.MediaType ?? "");

        // 2. Key exceeding 100 characters on transfer
        var longKey = new string('k', 101);
        var longKeyReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/transfer", token);
        longKeyReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: longKey));
        var longKeyResp = await client.SendAsync(longKeyReq);
        Assert.Equal(HttpStatusCode.BadRequest, longKeyResp.StatusCode);
    }

    [Fact]
    public async Task Idempotency_SameKeyWithDifferentTargetOrOperation_ReturnsConflict409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, t3Id) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"conflict-idem-{Guid.NewGuid():N}";

        // Step 1: Successful transfer to T2
        var tReq1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        tReq1.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: key));
        var res1 = await client.SendAsync(tReq1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Step 2: Same key with DIFFERENT target table (T3) -> 409 Conflict
        var tReq2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/transfer", token);
        tReq2.Content = JsonContent.Create(new TransferSessionRequest(t3Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: key));
        var res2 = await client.SendAsync(tReq2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);

        // Step 3: Open session at T3, attempt Merge with same key -> 409 Conflict (different operation type)
        var o3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t3Id}/sessions", token);
        o3.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s3 = await (await client.SendAsync(o3)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var mReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s3!.Id}/merge", token);
        mReq.Content = JsonContent.Create(new MergeSessionRequest(s1.Id, s3.ConcurrencyToken, s1.ConcurrencyToken, IdempotencyKey: key));
        var res3 = await client.SendAsync(mReq);
        Assert.Equal(HttpStatusCode.Conflict, res3.StatusCode);
    }

    [Fact]
    public async Task InactiveOrOccupiedTargetTable_RejectedWith400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, t3Id) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // 1. Deactivate T2 then attempt transfer -> 400 Bad Request
        var t2Get = await (await client.SendAsync(RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}", token))).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);
        var deactReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/deactivate", token);
        deactReq.Headers.Add("If-Match", $"\"{t2Get!.ConcurrencyToken}\"");
        await client.SendAsync(deactReq);

        var transInactiveReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        transInactiveReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"inact-{Guid.NewGuid():N}"));
        var inactRes = await client.SendAsync(transInactiveReq);
        Assert.Equal(HttpStatusCode.BadRequest, inactRes.StatusCode);

        // 2. Occupy T3, then attempt transfer of s1 to T3 -> 400 Bad Request
        var o3 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t3Id}/sessions", token);
        o3.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        await client.SendAsync(o3);

        var transOccupiedReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/transfer", token);
        transOccupiedReq.Content = JsonContent.Create(new TransferSessionRequest(t3Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"occ-{Guid.NewGuid():N}"));
        var occRes = await client.SendAsync(transOccupiedReq);
        Assert.Equal(HttpStatusCode.BadRequest, occRes.StatusCode);
    }

    [Fact]
    public async Task BillRequestedSession_TransferAndMerge_RejectedWith400()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Activate and Request Bill
        var actReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/activate", token);
        actReq.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        var actRes = await (await client.SendAsync(actReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var billReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/request-bill", token);
        billReq.Headers.Add("If-Match", $"\"{actRes!.ConcurrencyToken}\"");
        var billRes = await (await client.SendAsync(billReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Transfer rejected
        var transReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/transfer", token);
        transReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: billRes!.ConcurrencyToken, IdempotencyKey: $"bill-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(transReq)).StatusCode);

        // Open s2 on T2 and attempt merge
        var o2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        o2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(o2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var mReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/merge", token);
        mReq.Content = JsonContent.Create(new MergeSessionRequest(s2!.Id, billRes.ConcurrencyToken, s2.ConcurrencyToken, IdempotencyKey: $"mbill-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(mReq)).StatusCode);
    }

    [Fact]
    public async Task Merge_StaleSourceOrTargetToken_RejectedWith409()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var o2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        o2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(o2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // 1. Stale source token
        var mReq1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/merge", token);
        mReq1.Content = JsonContent.Create(new MergeSessionRequest(s2!.Id, Guid.NewGuid(), s2.ConcurrencyToken, IdempotencyKey: $"stale-s-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(mReq1)).StatusCode);

        // 2. Stale target token
        var mReq2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/merge", token);
        mReq2.Content = JsonContent.Create(new MergeSessionRequest(s2.Id, s1.ConcurrencyToken, Guid.NewGuid(), IdempotencyKey: $"stale-t-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(mReq2)).StatusCode);
    }

    [Fact]
    public async Task SuspendedBranch_TransferAndMerge_Rejected()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Suspend branch directly via database to simulate lifecycle transition
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE tenancy.branches SET status = 'Suspended' WHERE id = @bid;";
            cmd.Parameters.AddWithValue("bid", branchId);
            await cmd.ExecuteNonQueryAsync();
        }

        // Transfer must be blocked on suspended branch
        var transReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/transfer", token);
        transReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, ConcurrencyToken: s1.ConcurrencyToken, IdempotencyKey: $"susp-{Guid.NewGuid():N}"));
        var transRes = await client.SendAsync(transReq);
        Assert.True(transRes.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task MergedSession_SourceCustomerJwt_RejectedPostMerge()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var o1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        o1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(o1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var o2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        o2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(o2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        // Generate customer token for s1
        var customerToken = RestaurantConfigTestHelpers.GenerateCustomerToken(tenantId, branchId, s1!.Id);

        // Pre-merge: customer token accesses runtime menu successfully
        var preMergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var preMergeRes = await client.SendAsync(preMergeReq);
        Assert.Equal(HttpStatusCode.OK, preMergeRes.StatusCode);

        // Merge s1 into s2
        var mReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/merge", token);
        mReq.Content = JsonContent.Create(new MergeSessionRequest(s2!.Id, s1.ConcurrencyToken, s2.ConcurrencyToken, IdempotencyKey: $"jwt-merge-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(mReq)).StatusCode);

        // Post-merge: s1's customer token MUST BE REJECTED with 401 Unauthorized
        var postMergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Get, $"/api/v1/catalog/branches/{branchId}/runtime-menu", customerToken);
        var postMergeRes = await client.SendAsync(postMergeReq);
        Assert.Equal(HttpStatusCode.Unauthorized, postMergeRes.StatusCode);
    }
}
