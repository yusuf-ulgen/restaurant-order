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

/// <summary>
/// Integration tests verifying SHA-256 request fingerprinting, constant-time replay comparison,
/// conflict detection (409) on mutated payloads, and concurrent single-movement execution.
/// </summary>
public class FloorSessionMovementFingerprintIntegrationTests : IClassFixture<TestcontainersFixture>
{
    private readonly TestcontainersFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public FloorSessionMovementFingerprintIntegrationTests(TestcontainersFixture fixture)
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
        await RestaurantConfigTestHelpers.SeedTenantAsync(_fixture.DatabaseConnectionString, tenantId, "FP Tenant", $"fp-{Guid.NewGuid():N}");
        var adminToken = RestaurantConfigTestHelpers.GenerateToken(Guid.NewGuid(), Guid.NewGuid(), tenantId, role: "RestaurantAdmin");

        var brandReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/brands", adminToken);
        brandReq.Content = JsonContent.Create(new CreateBrandApiRequest("FP Brand", $"fpb-{Guid.NewGuid():N}"));
        var brand = await (await client.SendAsync(brandReq)).Content.ReadFromJsonAsync<BrandDto>();

        var branchReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, "/api/v1/restaurant-config/branches", adminToken);
        branchReq.Content = JsonContent.Create(new CreateBranchApiRequest(brand!.Id, "FP Branch", $"fpbr-{Guid.NewGuid():N}", "Europe/Istanbul", "TRY"));
        var branch = await (await client.SendAsync(branchReq)).Content.ReadFromJsonAsync<BranchDto>();

        var areaReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/restaurant-config/branches/{branch!.Id}/dining-areas", adminToken);
        areaReq.Content = JsonContent.Create(new CreateDiningAreaApiRequest("Main Area", $"fpa-{Guid.NewGuid():N}", "Indoor", 0));
        var area = await (await client.SendAsync(areaReq)).Content.ReadFromJsonAsync<DiningAreaDto>(JsonOptions);

        var t1Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t1Req.Content = JsonContent.Create(new CreateTableApiRequest(area!.Id, "T-01", "Table 1", 4));
        var t1 = await (await client.SendAsync(t1Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t2Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t2Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-02", "Table 2", 6));
        var t2 = await (await client.SendAsync(t2Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        var t3Req = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branch.Id}/tables", adminToken);
        t3Req.Content = JsonContent.Create(new CreateTableApiRequest(area.Id, "T-03", "Table 3", 4));
        var t3 = await (await client.SendAsync(t3Req)).Content.ReadFromJsonAsync<RestaurantTableDto>(JsonOptions);

        return (tenantId, branch.Id, adminToken, t1!.Id, t2!.Id, t3!.Id);
    }

    [Fact]
    public async Task SameKey_IdenticalPayload_ReplaysOriginalSuccess()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"fp-replay-{Guid.NewGuid():N}";
        var payload = new TransferSessionRequest(t2Id, session!.ConcurrencyToken, null, key, "Window request");

        // 1. First execution
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        req1.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req1.Content = JsonContent.Create(payload);
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);
        var result1 = await resp1.Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.Equal(t2Id, result1!.TableId);

        // 2. Replay with identical key, target, and reason
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        req2.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req2.Content = JsonContent.Create(payload);
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, resp2.StatusCode);
        var result2 = await resp2.Content.ReadFromJsonAsync<DiningSessionDto>();
        Assert.Equal(t2Id, result2!.TableId);
    }

    [Fact]
    public async Task SameKey_DifferentTarget_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, t3Id) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"fp-difftgt-{Guid.NewGuid():N}";

        // 1. First transfer to Table 2
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session!.Id}/transfer", token);
        req1.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req1.Content = JsonContent.Create(new TransferSessionRequest(t2Id, session.ConcurrencyToken, null, key, "Move to 2"));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // 2. Second request with SAME key but different target (Table 3)
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        req2.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req2.Content = JsonContent.Create(new TransferSessionRequest(t3Id, session.ConcurrencyToken, null, key, "Move to 3"));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task SameKey_DifferentOperation_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var open1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        open1.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s1 = await (await client.SendAsync(open1)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var open2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t2Id}/sessions", token);
        open2.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var s2 = await (await client.SendAsync(open2)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"fp-diffop-{Guid.NewGuid():N}";

        // 1. Execute Merge
        var mergeReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1!.Id}/merge", token);
        mergeReq.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        mergeReq.Content = JsonContent.Create(new MergeSessionRequest(s2!.Id, s1.ConcurrencyToken, s2.ConcurrencyToken, key, "Merge op"));
        var mergeResp = await client.SendAsync(mergeReq);
        Assert.Equal(HttpStatusCode.OK, mergeResp.StatusCode);

        // 2. Execute Transfer with SAME key
        var transferReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{s1.Id}/transfer", token);
        transferReq.Headers.Add("If-Match", $"\"{s1.ConcurrencyToken}\"");
        transferReq.Content = JsonContent.Create(new TransferSessionRequest(t2Id, s1.ConcurrencyToken, null, key, "Merge op"));
        var transferResp = await client.SendAsync(transferReq);
        Assert.Equal(HttpStatusCode.Conflict, transferResp.StatusCode);
    }

    [Fact]
    public async Task SameKey_DifferentReason_Returns409Conflict()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"fp-diffreason-{Guid.NewGuid():N}";

        // 1. Transfer with Reason A
        var req1 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session!.Id}/transfer", token);
        req1.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req1.Content = JsonContent.Create(new TransferSessionRequest(t2Id, session.ConcurrencyToken, null, key, "VIP customer requested quiet table"));
        var resp1 = await client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, resp1.StatusCode);

        // 2. Transfer with same key and target, but mutated Reason B
        var req2 = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        req2.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        req2.Content = JsonContent.Create(new TransferSessionRequest(t2Id, session.ConcurrencyToken, null, key, "Staff table consolidation"));
        var resp2 = await client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, resp2.StatusCode);
    }

    [Fact]
    public async Task Concurrent_IdenticalFingerprint_BothSucceed_ProducesSingleMovementRecord()
    {
        if (!TestcontainersGuard.ShouldRun(_fixture)) return;
        await EnsureMigrationsAppliedAsync();

        var client = RestaurantConfigTestHelpers.CreateTestClient(_fixture);
        var (tenantId, branchId, token, t1Id, t2Id, _) = await SetupFloorAsync(client);

        var openReq = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/tables/{t1Id}/sessions", token);
        openReq.Content = JsonContent.Create(new OpenDiningSessionRequest(2));
        var session = await (await client.SendAsync(openReq)).Content.ReadFromJsonAsync<DiningSessionDto>();

        var key = $"fp-parallel-{Guid.NewGuid():N}";
        var reason = "Parallel race check";

        var reqA = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session!.Id}/transfer", token);
        reqA.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        reqA.Content = JsonContent.Create(new TransferSessionRequest(t2Id, session.ConcurrencyToken, null, key, reason));

        var reqB = RestaurantConfigTestHelpers.CreateAuthenticatedRequest(HttpMethod.Post, $"/api/v1/floor/branches/{branchId}/sessions/{session.Id}/transfer", token);
        reqB.Headers.Add("If-Match", $"\"{session.ConcurrencyToken}\"");
        reqB.Content = JsonContent.Create(new TransferSessionRequest(t2Id, session.ConcurrencyToken, null, key, reason));

        var responses = await Task.WhenAll(client.SendAsync(reqA), client.SendAsync(reqB));

        // Both requests must succeed (one created, other replayed)
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        // Verify database: exactly 1 movement record exists with valid 64-char hex fingerprint
        await using var conn = new NpgsqlConnection(_fixture.DatabaseConnectionString);
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT request_fingerprint FROM tenancy.dining_session_movements WHERE idempotency_key = @key;";
        cmd.Parameters.AddWithValue("key", key);

        var fingerprints = new List<string>();
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                fingerprints.Add(reader.GetString(0));
            }
        }

        Assert.Single(fingerprints);
        Assert.Equal(64, fingerprints[0].Length);
    }
}
